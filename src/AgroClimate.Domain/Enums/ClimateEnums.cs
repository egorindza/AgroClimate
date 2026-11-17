namespace AgroClimate.Domain.Enums;

public enum FacilityType
{
    Greenhouse = 0,
    CityFarm = 1,
    VegetableStorage = 2,
    GrainSilo = 3,
    BioLabChamber = 4
}

public enum ComplianceState
{
    Optimal = 0,
    Warning = 1,
    Critical = 2,
    Offline = 3
}

public enum IncidentSeverity
{
    Warning = 1,
    Critical = 2,
    Emergency = 3
}

public enum IncidentStatus
{
    Open = 0,
    Acknowledged = 1,
    Mitigating = 2,
    Resolved = 3
}

public enum IncidentTriggerType
{
    ThresholdBreach = 0,
    RapidTrendAnomaly = 1,
    SelfHeatingRisk = 2,
    DeadMansSwitch = 3,
    CondensationBlightRisk = 4
}

public enum TimelineEventType
{
    IncidentOpened = 0,
    Acknowledged = 1,
    ChecklistUpdated = 2,
    EngineerComment = 3,
    SeverityEscalated = 4,
    TrendAlert = 5,
    IncidentResolved = 6
}

public enum SimulationScenario
{
    NormalOperation = 0,
    IrkutskFrostHeatingFailure = 1,
    GrainSiloSelfHeating = 2,
    PotatoStorageDeadMansSwitch = 3
}
