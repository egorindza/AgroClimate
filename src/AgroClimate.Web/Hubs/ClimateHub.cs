using AgroClimate.Application.DTOs;
using AgroClimate.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AgroClimate.Web.Hubs;

public sealed class ClimateHub : Hub
{
    public async Task JoinIncidentWarRoom(int incidentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"incident-{incidentId}");
    }

    public async Task LeaveIncidentWarRoom(int incidentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"incident-{incidentId}");
    }
}

public sealed class SignalRClimateNotifier : IClimateRealtimeNotifier
{
    private readonly IHubContext<ClimateHub> _hubContext;

    public SignalRClimateNotifier(IHubContext<ClimateHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task BroadcastZoneTelemetryAsync(ZoneLiveTelemetryDto telemetry, PipelineMetricsDto pipeline, CancellationToken ct = default)
    {
        return _hubContext.Clients.All.SendAsync("ReceiveZoneTelemetry", telemetry, pipeline, ct);
    }

    public Task BroadcastIncidentEventAsync(IncidentRealtimeEventDto incidentEvent, CancellationToken ct = default)
    {
        return _hubContext.Clients.All.SendAsync("ReceiveIncidentEvent", incidentEvent, ct);
    }

    public Task BroadcastSimulatorStatusAsync(SimulatorStatusDto status, CancellationToken ct = default)
    {
        return _hubContext.Clients.All.SendAsync("ReceiveSimulatorStatus", status, ct);
    }
}
