using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;

namespace AgroClimate.Infrastructure.Pipeline;

/// <summary>
/// Высокопроизводительный потокобезопасный буфер телеметрии на базе System.Threading.Channels.
/// Развязывает прием JSON-пакетов от микроконтроллеров ESP32/симулятора и пакетную запись в MS SQL Server.
/// </summary>
public sealed class TelemetryChannelBuffer : ITelemetryChannel
{
    private readonly Channel<TelemetryPacketEnvelope> _channel;

    private long _totalEnqueued;
    private long _totalPersistedRows;
    private long _totalBatchesFlushed;
    private long _lastFlushDurationMicros;
    private int _lastBatchSize;
    private long _columnstoreTotalRows;

    public TelemetryChannelBuffer()
    {
        var options = new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<TelemetryPacketEnvelope>(options);
    }

    public async ValueTask EnqueueAsync(TelemetryPacketEnvelope packet, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(packet, ct);
        Interlocked.Increment(ref _totalEnqueued);
    }

    public bool TryEnqueue(TelemetryPacketEnvelope packet)
    {
        if (_channel.Writer.TryWrite(packet))
        {
            Interlocked.Increment(ref _totalEnqueued);
            return true;
        }

        return false;
    }

    public async IAsyncEnumerable<IReadOnlyList<TelemetryPacketEnvelope>> ReadBatchesAsync(
        int maxBatchSize,
        TimeSpan maxWait,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = _channel.Reader;

        while (await reader.WaitToReadAsync(ct))
        {
            var batch = new List<TelemetryPacketEnvelope>(Math.Min(maxBatchSize, 64));

            while (batch.Count < maxBatchSize && reader.TryRead(out var item))
            {
                batch.Add(item);
            }

            // Короткое окно коалесценции пакета, чтобы объединить показания всех зон в один SqlBulkCopy
            if (batch.Count < maxBatchSize && maxWait > TimeSpan.Zero)
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                delayCts.CancelAfter(maxWait);

                try
                {
                    while (batch.Count < maxBatchSize && await reader.WaitToReadAsync(delayCts.Token))
                    {
                        while (batch.Count < maxBatchSize && reader.TryRead(out var more))
                        {
                            batch.Add(more);
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Таймер коалесценции истек — отдаем накопленный батч
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }
    }

    public PipelineMetricsDto GetMetrics()
    {
        int depth = _channel.Reader.CanCount ? _channel.Reader.Count : 0;
        double lastMs = Interlocked.Read(ref _lastFlushDurationMicros) / 1000.0;

        return new PipelineMetricsDto(
            QueueDepth: depth,
            TotalEnqueued: Interlocked.Read(ref _totalEnqueued),
            TotalPersistedRows: Interlocked.Read(ref _totalPersistedRows),
            TotalBatchesFlushed: Interlocked.Read(ref _totalBatchesFlushed),
            LastFlushDurationMs: Math.Round(lastMs, 2),
            LastBatchSize: Volatile.Read(ref _lastBatchSize),
            ColumnstoreTotalRows: Interlocked.Read(ref _columnstoreTotalRows));
    }

    public void RecordBatchFlushed(int count, double durationMs, long columnstoreTotalRows)
    {
        Interlocked.Add(ref _totalPersistedRows, count);
        Interlocked.Increment(ref _totalBatchesFlushed);
        Interlocked.Exchange(ref _lastFlushDurationMicros, (long)(durationMs * 1000));
        Volatile.Write(ref _lastBatchSize, count);
        if (columnstoreTotalRows >= 0)
        {
            Interlocked.Exchange(ref _columnstoreTotalRows, columnstoreTotalRows);
        }
    }
}
