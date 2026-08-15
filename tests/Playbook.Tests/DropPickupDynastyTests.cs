using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Intelligence.Services;

namespace Playbook.Tests;

/// <summary>
/// Covers the ImmediateValue/DynastyValue split: DynastyValue only applies for Dynasty leagues,
/// never lets a single week's projection swing overwhelm long-horizon value, treats temporary
/// injuries and missing signals as neutral (not destructive), and still allows a genuinely
/// unremarkable veteran to be a legitimate drop.
/// </summary>
public class DropPickupDynastyTests
{
    private static readonly Guid LeagueId = Guid.NewGuid();

    [Fact]
    public void Dynasty_And_Redraft_Produce_Different_Valuation_For_The_Same_Roster()
    {
        var youngStarter = MakePlayer(Position.WR, "Young Starter", age: 22);
        var team = MakeTeam([youngStarter.Id], starterIds: [youngStarter.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [youngStarter.Id] = MakeProjection(youngStarter.Id, points: 10, confidence: 50)
        };

        var redraft = CreateService([youngStarter], projections, team, [], LeagueType.Redraft);
        var dynasty = CreateService([youngStarter], projections, team, [], LeagueType.Dynasty);

        var redraftCandidate = Assert.Single(redraft.GetReport().RosterAssessment);
        var dynastyCandidate = Assert.Single(dynasty.GetReport().RosterAssessment);

        // Immediate value is identical (same formula, same inputs) regardless of league type.
        Assert.Equal(18.0, redraftCandidate.ImmediateValue, 3);
        Assert.Equal(18.0, dynastyCandidate.ImmediateValue, 3);

        // Only the Dynasty league computes DynastyValue and folds it into the ranking score.
        Assert.Null(redraftCandidate.DynastyValue);
        Assert.NotNull(dynastyCandidate.DynastyValue);
        Assert.Equal(16.5, dynastyCandidate.DynastyValue!.Value, 3);

        Assert.Equal(18.0, redraftCandidate.KeepValueScore, 3);
        Assert.Equal(25.5, dynastyCandidate.KeepValueScore, 3);
    }

    [Fact]
    public void Young_Starter_With_Temporary_Injury_Is_Protected_In_Dynasty()
    {
        var target = MakePlayer(Position.RB, "Young Injured Starter", age: 22);
        var filler1 = MakePlayer(Position.RB, "Filler RB 1");
        var filler2 = MakePlayer(Position.RB, "Filler RB 2");
        var freeAgent = MakePlayer(Position.RB, "Better FA RB");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id], starterIds: [target.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 3, confidence: 50), // soft week
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 8, confidence: 50)
        };
        var injuries = new Dictionary<Guid, PlayerInjuryRecord>
        {
            [target.Id] = MakeInjury(target.Id, InjurySeverity.Minor)
        };

        var service = CreateService(
            [target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty, injuries);

        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(-3.0, candidate.ImmediateValue, 3);
        Assert.Equal(8.5, candidate.DynastyValue!.Value, 3);
        Assert.Equal(7.0, candidate.KeepValueScore, 3);
        Assert.Equal(DropPickupClassification.Protected, candidate.Classification);
    }

    [Fact]
    public void Extreme_Projection_Swing_Does_Not_Overwhelm_Dynasty_Value()
    {
        var target = MakePlayer(Position.RB, "Young Starter Bad Week", age: 22);
        var filler1 = MakePlayer(Position.RB, "Filler RB 1");
        var filler2 = MakePlayer(Position.RB, "Filler RB 2");
        var freeAgent = MakePlayer(Position.RB, "Huge Week FA RB");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id], starterIds: [target.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 0, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 20, confidence: 50)
        };

        var dynasty = CreateService([target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty);
        var redraft = CreateService([target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Redraft);

        var dynastyCandidate = dynasty.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);
        var redraftCandidate = redraft.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        // Same brutal weekly swing (-18 immediate) is an outright Drop under Redraft...
        Assert.Equal(-18.0, redraftCandidate.KeepValueScore, 3);
        Assert.Equal(DropPickupClassification.DropCompetitive, redraftCandidate.Classification);

        // ...but Dynasty's long-horizon value keeps the same swing from being decisive.
        Assert.Equal(0.5, dynastyCandidate.KeepValueScore, 3);
        Assert.NotEqual(DropPickupClassification.DropCompetitive, dynastyCandidate.Classification);
    }

    [Fact]
    public void Older_Unremarkable_Player_Can_Still_Be_A_Legitimate_Dynasty_Drop()
    {
        var target = MakePlayer(Position.WR, "Aging Bench WR", age: 34);
        var filler1 = MakePlayer(Position.WR, "Filler WR 1");
        var filler2 = MakePlayer(Position.WR, "Filler WR 2");
        var filler3 = MakePlayer(Position.WR, "Filler WR 3");
        var freeAgent = MakePlayer(Position.WR, "Slightly Better FA WR");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id, filler3.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 4, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 7, confidence: 50)
        };

        var service = CreateService(
            [target, filler1, filler2, filler3, freeAgent], projections, team, [], LeagueType.Dynasty);

        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        // 4 WRs rostered, 0 current starters at the position — 1 beyond the normal allowance
        // (0 starters + 3-bench baseline = 3), so roster pressure adds -2.5 on top of the raw
        // age/role/injury/scarcity/waiver total of -6.0.
        Assert.Equal(-3.0, candidate.ImmediateValue, 3);
        Assert.Equal(-8.5, candidate.DynastyValue!.Value, 3);
        Assert.Equal(-2.5, candidate.RosterPressure!.Value, 3);
        Assert.Equal(-10.0, candidate.KeepValueScore, 3);
        Assert.Equal(DropPickupClassification.DropCompetitive, candidate.Classification);
    }

    [Fact]
    public void Missing_Dynasty_Signals_Contribute_Zero_Not_A_Penalty()
    {
        // 3 TEs with 2 current starters at the position is within the normal allowance
        // (2 starters + 1-bench baseline = 3) — no surplus pressure on the non-starter target —
        // so this stays a clean test of every other component being neutral.
        var target = MakePlayer(Position.TE, "Unknown Age TE", age: null);
        var filler1 = MakePlayer(Position.TE, "Filler TE 1");
        var filler2 = MakePlayer(Position.TE, "Filler TE 2");
        var freeAgent = MakePlayer(Position.TE, "FA TE");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id], starterIds: [filler1.Id, filler2.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 5, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 6, confidence: 50)
        };

        // No injury record for target: GetCurrentInjury returns null (never fabricated as "healthy").
        var service = CreateService(
            [target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty);

        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        // No age on file, not a starter, adequate depth, no injury, neutral confidence, no surplus,
        // and a replacement margin too small to trigger the waiver-protection bonus: every
        // DynastyValue component is legitimately absent/neutral, so the total is exactly zero.
        Assert.Equal(0.0, candidate.DynastyValue!.Value, 3);
        Assert.Equal(0.0, candidate.RosterPressure!.Value, 3);
    }

    [Fact]
    public void Hold_Classified_Dynasty_Asset_Never_Appears_As_Suggestion_Or_Trade_Candidate()
    {
        // Young fantasy-starter RB having a soft week, with a hotter waiver RB available. Old
        // (buggy) behavior: KeepValueScore is still among the roster's lowest, so it gets offered
        // as a drop purely because a better free agent exists this week — even though DynastyValue
        // clearly makes this a Hold.
        var target = MakePlayer(Position.RB, "Young Starter Soft Week", age: 22);
        var filler = MakePlayer(Position.RB, "RB Filler");
        var freeAgent = MakePlayer(Position.RB, "Hot Waiver RB");
        var team = MakeTeam([target.Id, filler.Id], starterIds: [target.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 4, confidence: 50),
            [filler.Id] = MakeProjection(filler.Id, points: 12, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 10, confidence: 50)
        };

        var service = CreateService([target, filler, freeAgent], projections, team, [], LeagueType.Dynasty);
        var report = service.GetReport();
        var candidate = report.RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(DropPickupClassification.Protected, candidate.Classification);
        Assert.DoesNotContain(report.Suggestions, s => s.Drop.PlayerId == target.Id);
        Assert.DoesNotContain(report.TradeCandidates, c => c.PlayerId == target.Id);
    }

    [Fact]
    public void Trade_Classified_Dynasty_Asset_Surfaces_As_Trade_Candidate_Not_Suggestion()
    {
        var target = MakePlayer(Position.WR, "Replaceable Dynasty WR", age: 27);
        var filler = MakePlayer(Position.WR, "WR Filler");
        var freeAgent = MakePlayer(Position.WR, "Slightly Better FA WR");
        var team = MakeTeam([target.Id, filler.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 6, confidence: 50),
            [filler.Id] = MakeProjection(filler.Id, points: 9, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 8, confidence: 50)
        };

        var service = CreateService([target, filler, freeAgent], projections, team, [], LeagueType.Dynasty);
        var report = service.GetReport();
        var candidate = report.RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(DropPickupClassification.Trade, candidate.Classification);
        Assert.DoesNotContain(report.Suggestions, s => s.Drop.PlayerId == target.Id);
        Assert.Contains(report.TradeCandidates, c => c.PlayerId == target.Id);
    }

    [Fact]
    public void Genuine_Dynasty_Drop_Still_Produces_A_Suggestion()
    {
        var target = MakePlayer(Position.TE, "Aging Unremarkable TE", age: 34);
        var filler = MakePlayer(Position.TE, "TE Filler");
        var freeAgent = MakePlayer(Position.TE, "Better FA TE");
        var team = MakeTeam([target.Id, filler.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 4, confidence: 50),
            [filler.Id] = MakeProjection(filler.Id, points: 9, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 8, confidence: 50)
        };

        var service = CreateService([target, filler, freeAgent], projections, team, [], LeagueType.Dynasty);
        var report = service.GetReport();
        var candidate = report.RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(DropPickupClassification.DropCompetitive, candidate.Classification);
        Assert.Contains(report.Suggestions, s => s.Drop.PlayerId == target.Id && s.Pickup.PlayerId == freeAgent.Id);
    }

    [Fact]
    public void No_Suggestions_Are_Manufactured_When_No_Dynasty_Roster_Player_Is_A_Genuine_Drop()
    {
        // A roster made up entirely of Hold/Trade-classified dynasty assets, each with a better
        // free agent available this week, must never be padded into drop suggestions — the old
        // "take the bottom N by score" logic would have produced up to MaxSuggestions swaps here.
        var holdTarget = MakePlayer(Position.RB, "Hold RB", age: 22);
        var holdFiller = MakePlayer(Position.RB, "Hold RB Filler");
        var holdFreeAgent = MakePlayer(Position.RB, "Hold RB FA");
        var tradeTarget = MakePlayer(Position.WR, "Trade WR", age: 27);
        var tradeFiller = MakePlayer(Position.WR, "Trade WR Filler");
        var tradeFreeAgent = MakePlayer(Position.WR, "Trade WR FA");
        var team = MakeTeam(
            [holdTarget.Id, holdFiller.Id, tradeTarget.Id, tradeFiller.Id],
            starterIds: [holdTarget.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [holdTarget.Id] = MakeProjection(holdTarget.Id, points: 4, confidence: 50),
            [holdFiller.Id] = MakeProjection(holdFiller.Id, points: 12, confidence: 50),
            [holdFreeAgent.Id] = MakeProjection(holdFreeAgent.Id, points: 10, confidence: 50),
            [tradeTarget.Id] = MakeProjection(tradeTarget.Id, points: 6, confidence: 50),
            [tradeFiller.Id] = MakeProjection(tradeFiller.Id, points: 20, confidence: 50),
            [tradeFreeAgent.Id] = MakeProjection(tradeFreeAgent.Id, points: 8, confidence: 50)
        };

        var service = CreateService(
            [holdTarget, holdFiller, holdFreeAgent, tradeTarget, tradeFiller, tradeFreeAgent],
            projections, team, [], LeagueType.Dynasty);
        var report = service.GetReport();

        Assert.Empty(report.Suggestions);
        Assert.Single(report.TradeCandidates);
        Assert.Equal(tradeTarget.Id, report.TradeCandidates[0].PlayerId);
    }

    // Reproduces the reported failure mode: a roster with a clear TE surplus (including aging TEs)
    // alongside a young starting RB having a soft week against a much hotter waiver option. Proves
    // the roster-context layer — not raw weekly projection gap — drives the drop ranking.
    [Fact]
    public void TE_Surplus_And_Relative_Age_Make_Aging_TEs_Droppable_While_Young_Starter_RB_Stays_Protected()
    {
        var (report, oldTe, midTe, teStarter, youngRb) = BuildSurplusRoster();

        var oldTeCandidate = report.RosterAssessment.Single(c => c.PlayerId == oldTe.Id);
        var midTeCandidate = report.RosterAssessment.Single(c => c.PlayerId == midTe.Id);
        var teStarterCandidate = report.RosterAssessment.Single(c => c.PlayerId == teStarter.Id);
        var youngRbCandidate = report.RosterAssessment.Single(c => c.PlayerId == youngRb.Id);

        // Surplus (1 TE beyond normal allowance) presses every TE; being older than the position's
        // roster average compounds it further for the oldest TE, offsets it for the youngest.
        Assert.True(oldTeCandidate.RosterPressure < 0);
        Assert.True(oldTeCandidate.RosterPressure < teStarterCandidate.RosterPressure);
        Assert.Equal(0.0, youngRbCandidate.RosterPressure!.Value, 3); // RB depth is within normal allowance

        Assert.Equal(DropPickupClassification.DropCompetitive, oldTeCandidate.Classification);
        Assert.Equal(DropPickupClassification.DropCompetitive, midTeCandidate.Classification);
        Assert.Equal(DropPickupClassification.Protected, teStarterCandidate.Classification);
        Assert.Equal(DropPickupClassification.Protected, youngRbCandidate.Classification);
    }

    [Fact]
    public void Young_RB_With_Worse_Raw_Weekly_Gap_Than_Surplus_TE_Still_Ranks_Less_Droppable()
    {
        var (report, oldTe, _, _, youngRb) = BuildSurplusRoster();

        var oldTeCandidate = report.RosterAssessment.Single(c => c.PlayerId == oldTe.Id);
        var youngRbCandidate = report.RosterAssessment.Single(c => c.PlayerId == youngRb.Id);

        // The RB's own weekly projection gap vs its best waiver option (-8) is worse than the aging
        // TE's (-3) — pure per-player scoring would rank the RB as more expendable. Roster context
        // reverses that: the TE's position is in surplus and he's older than his TE peers, the RB's
        // is not and he's younger than his (protected) peers.
        Assert.True(oldTeCandidate.ImmediateValue > youngRbCandidate.ImmediateValue);
        Assert.True(oldTeCandidate.KeepValueScore < youngRbCandidate.KeepValueScore);

        Assert.Contains(report.Suggestions, s => s.Drop.PlayerId == oldTe.Id);
        Assert.DoesNotContain(report.Suggestions, s => s.Drop.PlayerId == youngRb.Id);
    }

    [Fact]
    public void Roster_Structure_Not_Player_Identity_Determines_The_Outcome()
    {
        // Two rosters, identical in every structural respect (position, age, starter status, depth,
        // projections) but built from entirely different players/names/ids. If the algorithm used
        // any player-specific exception, these would diverge; they must not.
        var (reportA, oldTeA, midTeA, teStarterA, youngRbA) = BuildSurplusRoster();
        var (reportB, oldTeB, midTeB, teStarterB, youngRbB) = BuildSurplusRoster();

        var a = reportA.RosterAssessment.Single(c => c.PlayerId == oldTeA.Id);
        var b = reportB.RosterAssessment.Single(c => c.PlayerId == oldTeB.Id);
        Assert.Equal(a.KeepValueScore, b.KeepValueScore, 3);
        Assert.Equal(a.Classification, b.Classification);

        var aMid = reportA.RosterAssessment.Single(c => c.PlayerId == midTeA.Id);
        var bMid = reportB.RosterAssessment.Single(c => c.PlayerId == midTeB.Id);
        Assert.Equal(aMid.KeepValueScore, bMid.KeepValueScore, 3);

        var aStarter = reportA.RosterAssessment.Single(c => c.PlayerId == teStarterA.Id);
        var bStarter = reportB.RosterAssessment.Single(c => c.PlayerId == teStarterB.Id);
        Assert.Equal(aStarter.KeepValueScore, bStarter.KeepValueScore, 3);

        var aRb = reportA.RosterAssessment.Single(c => c.PlayerId == youngRbA.Id);
        var bRb = reportB.RosterAssessment.Single(c => c.PlayerId == youngRbB.Id);
        Assert.Equal(aRb.KeepValueScore, bRb.KeepValueScore, 3);
    }

    // Reproduces the reported failure mode from real-roster validation: a starting RB on a team
    // with heavy positional depth (10 RBs, 5 beyond normal allowance) was classified
    // Drop-Competitive purely because SurplusPenaltyPerExcessPlayer was uncapped and could swamp
    // the starter role bonus. These lock down the fixed hierarchy: surplus alone can never drop a
    // legitimate starter, but genuine aging bench depth still can, and a young non-starter asset
    // is protected on dynasty value even at the same surplus-heavy position.
    [Fact]
    public void Heavy_Positional_Surplus_Alone_Never_Drops_A_Legitimate_Starter()
    {
        var (report, starter, _, _) = BuildHeavySurplusRbRoster();

        var candidate = report.RosterAssessment.Single(c => c.PlayerId == starter.Id);

        Assert.NotEqual(DropPickupClassification.DropCompetitive, candidate.Classification);
        Assert.DoesNotContain(report.Suggestions, s => s.Drop.PlayerId == starter.Id);
    }

    [Fact]
    public void Genuine_Aging_Bench_Depth_At_The_Same_Surplus_Position_Still_Drops()
    {
        var (report, _, agingBench, _) = BuildHeavySurplusRbRoster();

        var candidate = report.RosterAssessment.Single(c => c.PlayerId == agingBench.Id);

        Assert.Equal(DropPickupClassification.DropCompetitive, candidate.Classification);
    }

    [Fact]
    public void Young_NonStarter_Asset_At_The_Same_Surplus_Position_Remains_Protected()
    {
        var (report, _, _, youngProspect) = BuildHeavySurplusRbRoster();

        var candidate = report.RosterAssessment.Single(c => c.PlayerId == youngProspect.Id);

        Assert.Equal(DropPickupClassification.Protected, candidate.Classification);
    }

    [Fact]
    public void Heavy_Surplus_Starter_Protection_Is_Deterministic_And_Not_Player_Specific()
    {
        var (reportA, starterA, _, _) = BuildHeavySurplusRbRoster();
        var (reportB, starterB, _, _) = BuildHeavySurplusRbRoster();

        var a = reportA.RosterAssessment.Single(c => c.PlayerId == starterA.Id);
        var b = reportB.RosterAssessment.Single(c => c.PlayerId == starterB.Id);

        Assert.Equal(a.KeepValueScore, b.KeepValueScore, 3);
        Assert.Equal(a.Classification, b.Classification);
        Assert.Equal(DropPickupClassification.Protected, a.Classification);
    }

    /// <summary>
    /// 10 rostered RBs (2 starters + 8 bench) against a normal allowance of 5 — a 5-player surplus,
    /// matching the real-roster scenario that exposed the uncapped-surplus bug. One starter near
    /// the position's average age, several older bench veterans, and one young non-starter prospect.
    /// </summary>
    private static (DropPickupReport Report, Player Starter, Player AgingBench, Player YoungProspect)
        BuildHeavySurplusRbRoster()
    {
        var starterA = MakePlayer(Position.RB, $"Starter A {Guid.NewGuid():N}", age: 29);
        var starterB = MakePlayer(Position.RB, $"Starter B {Guid.NewGuid():N}", age: 25);
        var bench = Enumerable.Range(0, 7)
            .Select(i => MakePlayer(Position.RB, $"Aging Bench {i} {Guid.NewGuid():N}", age: 32 + (i % 3)))
            .ToList();
        var youngProspect = MakePlayer(Position.RB, $"Young Prospect {Guid.NewGuid():N}", age: 22);
        var freeAgent = MakePlayer(Position.RB, $"FA RB {Guid.NewGuid():N}");

        var rosterIds = new List<Guid> { starterA.Id, starterB.Id, youngProspect.Id };
        rosterIds.AddRange(bench.Select(p => p.Id));
        var team = MakeTeam(rosterIds, starterIds: [starterA.Id, starterB.Id]);

        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [starterA.Id] = MakeProjection(starterA.Id, points: 9, confidence: 55),
            [starterB.Id] = MakeProjection(starterB.Id, points: 12, confidence: 60),
            [youngProspect.Id] = MakeProjection(youngProspect.Id, points: 6, confidence: 55),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 5, confidence: 50)
        };
        foreach (var b in bench)
        {
            projections[b.Id] = MakeProjection(b.Id, points: 4, confidence: 45);
        }

        var allPlayers = new List<Player> { starterA, starterB, youngProspect, freeAgent };
        allPlayers.AddRange(bench);

        var service = CreateService(allPlayers, projections, team, [], LeagueType.Dynasty);
        return (service.GetReport(), starterA, bench[0], youngProspect);
    }

    // Reproduces a second real-roster finding: a valuable young dynasty asset already correctly
    // placed on IR/reserve by the manager (real Sleeper data — team.ReservePlayerIds) was still
    // classified Drop-Competitive, driven by (a) being counted toward positional surplus like an
    // active bench player and (b) an uncapped severity-based injury penalty large enough to erase
    // his other DynastyValue components by itself. Neither should happen: IR isn't a normal bench
    // spot competing for depth, and a temporary injury shouldn't by itself make a valuable young
    // asset look expendable — ImmediateValue already reflects the short-term production hit.
    [Fact]
    public void Reserve_IR_Player_With_Significant_Injury_Is_Not_DropCompetitive()
    {
        var (report, reservePlayer, _) = BuildReserveEligibleRbRoster();

        var candidate = report.RosterAssessment.Single(c => c.PlayerId == reservePlayer.Id);

        Assert.NotEqual(DropPickupClassification.DropCompetitive, candidate.Classification);
    }

    [Fact]
    public void Reserve_IR_Player_Is_Never_Offered_As_A_Drop_Suggestion()
    {
        var (report, reservePlayer, _) = BuildReserveEligibleRbRoster();

        Assert.DoesNotContain(report.Suggestions, s => s.Drop.PlayerId == reservePlayer.Id);
    }

    [Fact]
    public void Reserve_IR_Player_Is_Excluded_From_Active_Positional_Depth_For_Everyone()
    {
        var (report, reservePlayer, activePeer) = BuildReserveEligibleRbRoster();

        var reserveCandidate = report.RosterAssessment.Single(c => c.PlayerId == reservePlayer.Id);
        var peerCandidate = report.RosterAssessment.Single(c => c.PlayerId == activePeer.Id);

        // 6 RBs total but 1 is on IR — active depth is 5, so surplus is computed off 5, not 6.
        Assert.Equal(5, reserveCandidate.PositionDepthOnRoster);
        Assert.Equal(5, peerCandidate.PositionDepthOnRoster);
    }

    [Fact]
    public void Major_Injury_Alone_Does_Not_Overwhelm_An_Otherwise_Neutral_Dynasty_Asset()
    {
        // Isolates the injury-cap fix from every other signal: normal (non-thin, non-surplus)
        // depth, no starter bonus, neutral age/confidence/replacement margin. Under the old
        // uncapped penalty (-8.0) this scores -8.0 and lands squarely on Drop-Competitive; the
        // cap (-2.0) keeps a temporary injury from being decisive by itself.
        var target = MakePlayer(Position.RB, "Injured Neutral RB", age: 27);
        var filler1 = MakePlayer(Position.RB, "Filler RB 1", age: 27);
        var filler2 = MakePlayer(Position.RB, "Filler RB 2", age: 27);
        var freeAgent = MakePlayer(Position.RB, "FA RB");
        var team = MakeTeam([target.Id, filler1.Id, filler2.Id]);
        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [target.Id] = MakeProjection(target.Id, points: 8, confidence: 50),
            [filler1.Id] = MakeProjection(filler1.Id, points: 8, confidence: 50),
            [filler2.Id] = MakeProjection(filler2.Id, points: 8, confidence: 50),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 8, confidence: 50)
        };
        var injuries = new Dictionary<Guid, PlayerInjuryRecord>
        {
            [target.Id] = MakeInjury(target.Id, InjurySeverity.Major)
        };

        var service = CreateService(
            [target, filler1, filler2, freeAgent], projections, team, [], LeagueType.Dynasty, injuries);
        var candidate = service.GetReport().RosterAssessment.Single(c => c.PlayerId == target.Id);

        Assert.Equal(-2.0, candidate.DynastyValue!.Value, 3);
        Assert.NotEqual(DropPickupClassification.DropCompetitive, candidate.Classification);
    }

    /// <summary>
    /// 6 rostered RBs: 1 starter, 4 active bench (surplus beyond the normal allowance of 5 would
    /// require 6+ active, so 5 active keeps this at the boundary — the reserve player pushes the
    /// raw count to 6, which is exactly the surplus this test proves gets recomputed once he's
    /// excluded), and 1 on IR/reserve with a real Significant injury.
    /// </summary>
    private static (DropPickupReport Report, Player ReservePlayer, Player ActivePeer) BuildReserveEligibleRbRoster()
    {
        var starter = MakePlayer(Position.RB, $"Starter RB {Guid.NewGuid():N}", age: 26);
        var bench = Enumerable.Range(0, 4)
            .Select(i => MakePlayer(Position.RB, $"Bench RB {i} {Guid.NewGuid():N}", age: 26))
            .ToList();
        var reservePlayer = MakePlayer(Position.RB, $"Reserve RB {Guid.NewGuid():N}", age: 24);
        var freeAgent = MakePlayer(Position.RB, $"FA RB {Guid.NewGuid():N}");

        var rosterIds = new List<Guid> { starter.Id, reservePlayer.Id };
        rosterIds.AddRange(bench.Select(p => p.Id));
        var team = MakeTeam(rosterIds, starterIds: [starter.Id], reserveIds: [reservePlayer.Id]);

        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [starter.Id] = MakeProjection(starter.Id, points: 10, confidence: 55),
            [reservePlayer.Id] = MakeProjection(reservePlayer.Id, points: 1, confidence: 60),
            [freeAgent.Id] = MakeProjection(freeAgent.Id, points: 6, confidence: 50)
        };
        foreach (var b in bench)
        {
            projections[b.Id] = MakeProjection(b.Id, points: 5, confidence: 50);
        }

        var injuries = new Dictionary<Guid, PlayerInjuryRecord>
        {
            [reservePlayer.Id] = MakeInjury(reservePlayer.Id, InjurySeverity.Significant)
        };

        var allPlayers = new List<Player> { starter, reservePlayer, freeAgent };
        allPlayers.AddRange(bench);

        var service = CreateService(allPlayers, projections, team, [], LeagueType.Dynasty, injuries);
        return (service.GetReport(), reservePlayer, bench[0]);
    }

    /// <summary>
    /// 1 starting RB + 1 bench RB (within normal RB allowance, no surplus) alongside 3 TEs with
    /// only 1 starter (1 beyond the TE normal allowance of 2 — a genuine surplus), ages spread so
    /// the oldest TE is well above the position's own roster average and the RB is well below his.
    /// The RB has a much larger raw weekly projection gap vs. his best waiver option than the aging
    /// TE does vs. his — on pure per-player scoring the RB would look more expendable.
    /// </summary>
    private static (DropPickupReport Report, Player OldTe, Player MidTe, Player TeStarter, Player YoungRb) BuildSurplusRoster()
    {
        var oldTe = MakePlayer(Position.TE, $"Aging TE {Guid.NewGuid():N}", age: 38);
        var midTe = MakePlayer(Position.TE, $"Mid TE {Guid.NewGuid():N}", age: 30);
        var teStarter = MakePlayer(Position.TE, $"Starter TE {Guid.NewGuid():N}", age: 25);
        var teFreeAgent = MakePlayer(Position.TE, $"FA TE {Guid.NewGuid():N}");
        var youngRb = MakePlayer(Position.RB, $"Young Starter RB {Guid.NewGuid():N}", age: 23);
        var rbFiller = MakePlayer(Position.RB, $"Filler RB {Guid.NewGuid():N}");
        var rbFreeAgent = MakePlayer(Position.RB, $"Hot Waiver RB {Guid.NewGuid():N}");

        var team = MakeTeam(
            [oldTe.Id, midTe.Id, teStarter.Id, youngRb.Id, rbFiller.Id],
            starterIds: [teStarter.Id, youngRb.Id]);

        var projections = new Dictionary<Guid, PlayerProjection>
        {
            [oldTe.Id] = MakeProjection(oldTe.Id, points: 4, confidence: 50),
            [midTe.Id] = MakeProjection(midTe.Id, points: 6, confidence: 50),
            [teStarter.Id] = MakeProjection(teStarter.Id, points: 8, confidence: 50),
            [teFreeAgent.Id] = MakeProjection(teFreeAgent.Id, points: 7, confidence: 50),
            [youngRb.Id] = MakeProjection(youngRb.Id, points: 6, confidence: 50),
            [rbFiller.Id] = MakeProjection(rbFiller.Id, points: 5, confidence: 50),
            [rbFreeAgent.Id] = MakeProjection(rbFreeAgent.Id, points: 14, confidence: 50)
        };

        var service = CreateService(
            [oldTe, midTe, teStarter, teFreeAgent, youngRb, rbFiller, rbFreeAgent],
            projections, team, [], LeagueType.Dynasty);

        return (service.GetReport(), oldTe, midTe, teStarter, youngRb);
    }

    private static DropPickupService CreateService(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Guid, PlayerProjection> projections,
        FantasyTeam? team,
        IReadOnlyList<FantasyTeam> otherTeams,
        LeagueType leagueType,
        IReadOnlyDictionary<Guid, PlayerInjuryRecord>? injuries = null)
    {
        var league = new League
        {
            Id = LeagueId,
            Name = "Test Dynasty League",
            Platform = LeaguePlatform.Sleeper,
            LeagueType = leagueType,
            ScoringType = ScoringType.Ppr,
            NumberOfTeams = 10,
            CurrentWeek = 1,
            Season = 2026,
            IsActive = true,
            DataSource = LeagueDataSource.Sleeper,
            SelectedRosterId = team?.RosterId
        };

        var leagueState = new FakeLeagueState(
            team is null ? null : league,
            team,
            team is null ? otherTeams : [team, .. otherTeams]);
        var playerService = new FakePlayerService(players);
        var projectionService = new FakeProjectionService(projections);
        var injuryService = new FakePlayerInjuryService(injuries ?? new Dictionary<Guid, PlayerInjuryRecord>());

        return new DropPickupService(leagueState, playerService, projectionService, injuryService);
    }

    private static Player MakePlayer(Position position, string name, int? age = null) => new()
    {
        Id = Guid.NewGuid(),
        FullName = name,
        FirstName = name,
        LastName = name,
        Position = position,
        Team = "FA",
        Age = age,
        Status = PlayerStatus.Active
    };

    private static PlayerProjection MakeProjection(Guid playerId, decimal points, int confidence) => new()
    {
        PlayerId = playerId,
        Week = 1,
        ScoringFormat = ScoringType.Ppr,
        ProjectedFantasyPoints = points,
        Floor = points,
        Median = points,
        Ceiling = points,
        Confidence = confidence,
        Volatility = 30,
        ProjectionReasoning = [],
        SupportingIntelligence = [],
        ProjectionTimestamp = DateTimeOffset.UtcNow,
        ProjectionVersion = "test",
        InputsUsed = new ProjectionInputsUsed()
    };

    private static PlayerInjuryRecord MakeInjury(Guid playerId, InjurySeverity severity) => new()
    {
        PlayerId = playerId,
        Date = DateTimeOffset.UtcNow,
        Status = "Questionable",
        Severity = severity,
        IsCurrent = true
    };

    private static FantasyTeam MakeTeam(
        IReadOnlyList<Guid> playerIds,
        IReadOnlyList<Guid>? starterIds = null,
        IReadOnlyList<Guid>? reserveIds = null) => new()
    {
        LeagueId = LeagueId,
        RosterId = 1,
        DisplayName = "My Team",
        PlayerIds = playerIds,
        StarterIds = starterIds ?? [],
        ReservePlayerIds = reserveIds ?? []
    };

    private sealed class FakeLeagueState : ILeagueState
    {
        private readonly League? _league;
        private readonly FantasyTeam? _team;
        private readonly IReadOnlyList<FantasyTeam> _allTeams;

        public FakeLeagueState(League? league, FantasyTeam? team, IReadOnlyList<FantasyTeam> allTeams)
        {
            _league = league;
            _team = team;
            _allTeams = allTeams;
        }

        public League? CurrentLeague => _league;
        public FantasyTeam? CurrentUserTeam => _team;
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<League> GetAllLeagues() => _league is null ? [] : [_league];
        public League? GetCurrentLeague() => _league;
        public void SelectLeague(Guid leagueId)
        {
        }

        public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) => _allTeams;
        public IReadOnlyList<FantasyTeam> GetCurrentTeams() => _allTeams;
        public FantasyTeam? FindTeamForPlayer(Guid playerId) =>
            _allTeams.FirstOrDefault(t => t.PlayerIds.Contains(playerId));
        public FantasyTeam? GetUserTeam(Guid leagueId) => _team;
        public FantasyTeam? GetCurrentUserTeam() => _team;
        public bool SelectUserTeam(Guid leagueId, int rosterId) => false;
        public Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
            string sleeperLeagueId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePlayerService : IPlayerService
    {
        private readonly IReadOnlyList<Player> _players;
        public FakePlayerService(IReadOnlyList<Player> players) => _players = players;
        public IReadOnlyList<Player> GetAllPlayers() => _players;
        public Player? GetPlayer(Guid playerId) => _players.FirstOrDefault(p => p.Id == playerId);
        public PlayerProfile? GetPlayerProfile(Guid playerId) => null;
        public IReadOnlyList<Player> SearchPlayers(string? query) => _players;
        public void Refresh()
        {
        }
    }

    private sealed class FakeProjectionService : IProjectionService
    {
        private readonly IReadOnlyDictionary<Guid, PlayerProjection> _projections;
        public FakeProjectionService(IReadOnlyDictionary<Guid, PlayerProjection> projections) =>
            _projections = projections;
        public string EngineVersion => "test";
        public PlayerProjection? GetProjection(Guid playerId) => _projections.GetValueOrDefault(playerId);
        public PlayerProjection? ProjectPlayer(Guid playerId) => GetProjection(playerId);
        public IReadOnlyList<PlayerProjection> GetAllProjections() => _projections.Values.ToList();
        public IReadOnlyList<PlayerProjection> GetTopProjections(int count = 8) =>
            _projections.Values.OrderByDescending(p => p.ProjectedFantasyPoints).Take(count).ToList();
        public PlayerProjectionComparison? ComparePlayers(Guid leftPlayerId, Guid rightPlayerId) => null;
        public IReadOnlyList<PlayerProjection> ProjectRoster(IEnumerable<Guid> playerIds) =>
            _projections.Values.Where(p => playerIds.Contains(p.PlayerId)).ToList();
        public void Refresh()
        {
        }

        public void Invalidate()
        {
        }
    }

    private sealed class FakePlayerInjuryService : Playbook.Application.Injuries.Interfaces.IPlayerInjuryService
    {
        private readonly IReadOnlyDictionary<Guid, PlayerInjuryRecord> _currentInjuries;

        public FakePlayerInjuryService(IReadOnlyDictionary<Guid, PlayerInjuryRecord> currentInjuries) =>
            _currentInjuries = currentInjuries;

        public Playbook.Application.Injuries.InjuryProviderCapabilities ActiveCapabilities =>
            Playbook.Application.Injuries.InjuryProviderCapabilities.MockCurrentOnly;
        public HistoricalDataStatus GlobalHistoricalDataStatus => HistoricalDataStatus.NotSupportedByProvider;
        public IReadOnlyList<PlayerInjuryRecord> GetAllInjuries() => _currentInjuries.Values.ToList();
        public IReadOnlyList<PlayerInjuryRecord> GetInjuriesForPlayer(Guid playerId) =>
            _currentInjuries.TryGetValue(playerId, out var record) ? [record] : [];
        public PlayerInjuryRecord? GetCurrentInjury(Guid playerId) => _currentInjuries.GetValueOrDefault(playerId);
        public IReadOnlyList<PlayerInjuryRecord> GetHistoricalInjuries(Guid playerId) => [];
        public PlayerInjuryProfile GetPlayerInjuryProfile(Guid playerId) => new() { PlayerId = playerId };
        public void Refresh()
        {
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
