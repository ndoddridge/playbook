namespace Playbook.Core.Knowledge;

/// <summary>Which situational layer a knowledge item describes.</summary>
public enum KnowledgeScope
{
    Player = 0,
    Team = 1,
    Matchup = 2,
    Context = 3
}

/// <summary>
/// Aspect of the situation. Only populate when source data exists;
/// otherwise mark explicitly unavailable.
/// </summary>
public enum KnowledgeAspect
{
    RecentProduction = 0,
    Usage = 1,
    Opportunity = 2,
    Role = 3,
    SnapShare = 4,
    TargetShare = 5,
    CarryShare = 6,
    DepthChart = 7,
    Health = 8,
    InjuryStatus = 9,
    HistoricalPerformance = 10,
    Trend = 11,
    Projection = 12,
    OffensiveEnvironment = 13,
    DefensiveEnvironment = 14,
    Pace = 15,
    ScoringEnvironment = 16,
    OpponentStrength = 17,
    RecentForm = 18,
    OpponentTendencies = 19,
    PositionalMatchup = 20,
    GameEnvironment = 21,
    HomeAway = 22,
    Rest = 23,
    Weather = 24,
    GameScript = 25,
    TeammateAvailability = 26,
    RoleChange = 27,
    News = 28,
    Coverage = 29,
    Volatility = 30,
    Outlook = 31
}

/// <summary>Prediction engines that may consume shared knowledge.</summary>
public enum PredictionType
{
    Generic = 0,
    StartSit = 1,
    QuickPick = 2,
    PlayerProjection = 3,
    OverUnder = 4,
    Touchdown = 5,
    Ranking = 6,
    Matchup = 7,
    PlayerPerformance = 8
}

/// <summary>Simple reliability of the evidence source (explainable, not ML).</summary>
public enum EvidenceReliability
{
    Unknown = 0,
    Low = 1,
    Moderate = 2,
    High = 3
}
