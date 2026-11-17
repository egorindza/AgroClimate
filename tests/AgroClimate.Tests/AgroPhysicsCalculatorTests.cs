using AgroClimate.Domain.Enums;
using AgroClimate.Domain.Services;

namespace AgroClimate.Tests;

public sealed class AgroPhysicsCalculatorTests
{
    [Fact]
    public void CalculateDewPointC_At100PercentHumidity_EqualsAirTemperature()
    {
        double dewPoint = AgroPhysicsCalculator.CalculateDewPointC(22.5, 100.0);
        Assert.Equal(22.5, dewPoint, precision: 1);
    }

    [Theory]
    [InlineData(24.0, 70.0, 18.0, 18.6)]   // Теплица томата
    [InlineData(3.5, 90.0, 1.8, 2.3)]      // Овощехранилище картофеля «Сарма»
    [InlineData(25.0, 30.0, 5.8, 6.6)]     // Сухой воздух
    [InlineData(-25.0, 80.0, -28.0, -27.0)] // Сибирский мороз Прибайкалья
    public void CalculateDewPointC_MatchesMagnusTetensPhysicalRange(
        double tempC,
        double humidityPercent,
        double expectedMin,
        double expectedMax)
    {
        double dp = AgroPhysicsCalculator.CalculateDewPointC(tempC, humidityPercent);
        Assert.InRange(dp, expectedMin, expectedMax);
    }

    [Fact]
    public void CalculateVpdKpa_At100PercentHumidity_ReturnsZero()
    {
        double vpd = AgroPhysicsCalculator.CalculateVpdKpa(21.0, 100.0);
        Assert.Equal(0.0, vpd, precision: 3);
    }

    [Theory]
    [InlineData(23.0, 68.0, 0.85, 0.96)]   // Оптимальная транспирация рассады томата
    [InlineData(3.4, 90.0, 0.06, 0.10)]    // Хранение семенного картофеля
    [InlineData(32.0, 35.0, 3.00, 3.20)]   // Засушливый тепловой стресс
    public void CalculateVpdKpa_MatchesTetensEquation(
        double tempC,
        double humidityPercent,
        double expectedMin,
        double expectedMax)
    {
        double vpd = AgroPhysicsCalculator.CalculateVpdKpa(tempC, humidityPercent);
        Assert.InRange(vpd, expectedMin, expectedMax);
    }

    [Fact]
    public void IsCondensationRisk_DetectsNarrowDewPointSpread()
    {
        double temp = 4.5;
        double dp = AgroPhysicsCalculator.CalculateDewPointC(temp, 95.0); // ~3.76°C (spread < 1°C)

        Assert.True(AgroPhysicsCalculator.IsCondensationRisk(temp, dp, minDewPointSpreadC: 1.5));
        Assert.False(AgroPhysicsCalculator.IsCondensationRisk(23.0, 15.0, minDewPointSpreadC: 1.8));
    }

    [Fact]
    public void EstimateMinutesToDamage_ShrinksUnderRapidCoolingAndCriticalOverrun()
    {
        int calmEta = AgroPhysicsCalculator.EstimateMinutesToDamage(
            currentTempC: 22.0,
            critTempMin: 12.0,
            critTempMax: 33.0,
            tempVelocityPerMin: 0.0,
            baseTimeToDamageMinutes: 40,
            triggerType: IncidentTriggerType.ThresholdBreach);

        int rapidCoolingEta = AgroPhysicsCalculator.EstimateMinutesToDamage(
            currentTempC: 18.0,
            critTempMin: 12.0,
            critTempMax: 33.0,
            tempVelocityPerMin: -1.5, // Падение на 1.5 °C/мин (до крит. порога 4 мин + тепловая инерция)
            baseTimeToDamageMinutes: 40,
            triggerType: IncidentTriggerType.RapidTrendAnomaly);

        int freezingOverrunEta = AgroPhysicsCalculator.EstimateMinutesToDamage(
            currentTempC: 6.0,
            critTempMin: 12.0,
            critTempMax: 33.0,
            tempVelocityPerMin: -1.2,
            baseTimeToDamageMinutes: 40,
            triggerType: IncidentTriggerType.ThresholdBreach);

        Assert.Equal(40, calmEta);
        Assert.True(rapidCoolingEta < calmEta);
        Assert.True(freezingOverrunEta < rapidCoolingEta);
    }
}
