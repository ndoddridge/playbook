namespace Playbook.Core.Draft;

/// <summary>
/// Preference strength for an explicit player preference. Guides the model; never absolute rank.
/// </summary>
public enum PreferenceStrength
{
    Medium = 1,
    High = 2,
    VeryHigh = 3
}

/// <summary>Declarative draft plan — intent, not a rigid tree. Missing data stays neutral.</summary>
public sealed class DraftStrategyPlan
{
    public required IReadOnlyList<PreferredPlayer> PreferredPlayers { get; init; }

    public required IReadOnlyList<string> Fades { get; init; }

    public required IReadOnlyList<ConditionalPreference> ConditionalPreferences { get; init; }

    public required IReadOnlyList<PhaseIntent> PhaseIntents { get; init; }

    public required IrStrategyRules IrStrategy { get; init; }

    public required SpecialLateRules SpecialLateRules { get; init; }

    /// <summary>Noah's live redraft companion defaults — preferences guide, intelligence decides.</summary>
    public static DraftStrategyPlan DefaultCompanion() => new()
    {
        PreferredPlayers =
        [
            new("Jonathan Taylor", PreferenceStrength.VeryHigh, "RB"),
            new("James Cook", PreferenceStrength.VeryHigh, "RB"),
            new("Saquon Barkley", PreferenceStrength.VeryHigh, "RB"),
            new("Chase Brown", PreferenceStrength.High, "RB"),
            new("Ashton Jeanty", PreferenceStrength.High, "RB"),
            new("Omarion Hampton", PreferenceStrength.High, "RB"),
            new("Derrick Henry", PreferenceStrength.Medium, "RB"),
            new("Chris Rodriguez", PreferenceStrength.High, "RB"),
            new("Travis Kelce", PreferenceStrength.VeryHigh, "TE"),
            new("George Kittle", PreferenceStrength.High, "TE"),
            new("Justin Herbert", PreferenceStrength.High, "QB"),
            new("Wan'Dale Robinson", PreferenceStrength.High, "WR"),
            new("Deebo Samuel", PreferenceStrength.Medium, "WR"),
            new("Chris Godwin", PreferenceStrength.High, "WR"),
            new("Jonathon Brooks", PreferenceStrength.High, "RB"),
            new("Zach Charbonnet", PreferenceStrength.High, "RB")
        ],
        Fades = ["Josh Downs", "Jordan Addison"],
        ConditionalPreferences =
        [
            new(
                "Quentin Johnston",
                "Ladd McConkey",
                ConditionalEffect.DowngradeWhenPresent,
                "Ladd McConkey already rostered reduces Johnston's unique upside.")
        ],
        PhaseIntents =
        [
            new(1, 2, ["RB"], "Strongly prefer RB unless a meaningful reason not to."),
            new(3, 4, ["WR"], "Strong WR priority to build the receiving core."),
            new(5, 6, ["RB", "WR"], "Balance the RB/WR core."),
            new(7, 8, ["QB", "TE"], "QB + TE strategy window."),
            new(9, 10, ["TE", "WR", "RB"], "Kelce / FLEX / TE branch — evaluate construction, don't blindly TE."),
            new(11, 30, ["WR", "RB", "TE", "QB"], "Upside + roster construction + IR strategy.")
        ],
        IrStrategy = new IrStrategyRules(
            MaxIrStashesToSkipKAndDst: 2,
            IrTargetNames: ["Jonathon Brooks", "Zach Charbonnet"]),
        SpecialLateRules = new SpecialLateRules(
            HerbertPlayerName: "Justin Herbert",
            KylerPlayerName: "Kyler Murray",
            LateRoundStart: 11)
    };
}

public sealed record PreferredPlayer(string PlayerName, PreferenceStrength Strength, string? PositionHint);

public sealed record ConditionalPreference(
    string PlayerName,
    string ConditionPlayerName,
    ConditionalEffect Effect,
    string Explanation);

public enum ConditionalEffect
{
    DowngradeWhenPresent = 0
}

public sealed record PhaseIntent(
    int FromRound,
    int ToRound,
    IReadOnlyList<string> PreferredPositions,
    string Intent);

public sealed record IrStrategyRules(
    int MaxIrStashesToSkipKAndDst,
    IReadOnlyList<string> IrTargetNames);

public sealed record SpecialLateRules(
    string HerbertPlayerName,
    string KylerPlayerName,
    int LateRoundStart);
