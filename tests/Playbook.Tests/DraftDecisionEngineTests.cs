using Playbook.Core.Draft;
using Playbook.Core.Players;
using Playbook.Infrastructure.Draft.Decision;

namespace Playbook.Tests;

public class DraftDecisionEngineTests
{
    [Fact]
    public void R1_R2_Strongly_Prefers_RB_Intent()
    {
        var state = BuildState(round: 1, roster: [], counts: EmptyCounts());
        var rb = Candidate("Jonathan Taylor", "RB", fit: 18m, upside: 24m);
        var wr = Candidate("Nico Collins", "WR", fit: 19m, upside: 25m);

        var slate = DraftDecisionEngine.Select([rb, wr], state, state.PositionalCounts);

        Assert.Equal("Jonathan Taylor", slate[0].Player.PlayerName);
        Assert.Equal(RecommendationRole.Primary, slate[0].Role);
    }

    [Fact]
    public void Preference_Order_Taylor_Over_Cook_Over_Saquon_When_Close()
    {
        var state = BuildState(round: 1, roster: [], counts: EmptyCounts());
        var taylor = Candidate("Jonathan Taylor", "RB", fit: 20.0m, upside: 26m);
        var cook = Candidate("James Cook", "RB", fit: 19.8m, upside: 25m);
        var saquon = Candidate("Saquon Barkley", "RB", fit: 19.6m, upside: 27m);

        var slate = DraftDecisionEngine.Select([saquon, cook, taylor], state, state.PositionalCounts);

        Assert.Equal("Jonathan Taylor", slate[0].Player.PlayerName);
    }

    [Fact]
    public void Meaningful_Construction_Gap_Can_Override_Preference()
    {
        // Already have 3 RBs — preferred RB sleeper should not beat a needed WR.
        var roster = new[]
        {
            Player("Jonathan Taylor", Position.RB),
            Player("James Cook", Position.RB),
            Player("David Montgomery", Position.RB),
            Player("Nico Collins", Position.WR)
        };
        var counts = Counts(rb: 3, wr: 1, qb: 0, te: 0);
        var state = BuildState(round: 5, roster: roster, counts: counts);

        var preferredRb = Candidate("Chris Rodriguez", "RB", fit: 14m, upside: 22m);
        var neededWr = Candidate("Ladd McConkey", "WR", fit: 13.5m, upside: 20m);

        var slate = DraftDecisionEngine.Select([preferredRb, neededWr], state, counts);

        Assert.Equal("Ladd McConkey", slate[0].Player.PlayerName);
        Assert.Equal(RecommendationRole.Primary, slate[0].Role);
    }

    [Fact]
    public void R3_R4_Strong_WR_Priority()
    {
        var roster = new[]
        {
            Player("Jonathan Taylor", Position.RB),
            Player("James Cook", Position.RB)
        };
        var counts = Counts(rb: 2, wr: 0, qb: 0, te: 0);
        var state = BuildState(round: 3, roster: roster, counts: counts);
        var rb = Candidate("Chase Brown", "RB", fit: 16m, upside: 22m);
        var wr = Candidate("Nico Collins", "WR", fit: 15.5m, upside: 21m);

        var slate = DraftDecisionEngine.Select([rb, wr], state, counts);

        Assert.Equal("Nico Collins", slate[0].Player.PlayerName);
    }

    [Fact]
    public void After_Kelce_Does_Not_Auto_Primary_Rb_Sleeper()
    {
        var roster = new[]
        {
            Player("Jonathan Taylor", Position.RB),
            Player("James Cook", Position.RB),
            Player("David Montgomery", Position.RB),
            Player("Nico Collins", Position.WR),
            Player("Ladd McConkey", Position.WR),
            Player("Chris Godwin", Position.WR),
            Player("Justin Herbert", Position.QB),
            Player("Travis Kelce", Position.TE),
            Player("Jonathon Brooks", Position.RB),
            Player("Zach Charbonnet", Position.RB)
        };
        var counts = Counts(rb: 3, wr: 3, qb: 1, te: 1);
        var state = BuildState(round: 11, roster: roster, counts: counts);

        var wandale = Candidate("Wan'Dale Robinson", "WR", fit: 11.5m, upside: 17m);
        var deebo = Candidate("Deebo Samuel", "WR", fit: 11.2m, upside: 16m);
        var rod = Candidate("Chris Rodriguez", "RB", fit: 12.5m, upside: 20m); // higher generic score

        var slate = DraftDecisionEngine.Select([rod, wandale, deebo], state, counts);

        Assert.NotEqual("Chris Rodriguez", slate[0].Player.PlayerName);
        Assert.Equal(RecommendationRole.Primary, slate[0].Role);
        Assert.Contains(slate, p => p.Player.PlayerName == "Chris Rodriguez"
                                    && p.Role is RecommendationRole.Upside or RecommendationRole.Alternative);
    }

    [Fact]
    public void Existing_Rb_Depth_Suppresses_Additional_Rb_As_Primary()
    {
        var roster = new[]
        {
            Player("Jonathan Taylor", Position.RB),
            Player("James Cook", Position.RB),
            Player("David Montgomery", Position.RB)
        };
        var counts = Counts(rb: 3, wr: 2, qb: 1, te: 0);
        var state = BuildState(round: 9, roster: roster, counts: counts);
        var rb = Candidate("Chris Rodriguez", "RB", fit: 13m, upside: 21m);
        var te = Candidate("George Kittle", "TE", fit: 12m, upside: 18m);

        var slate = DraftDecisionEngine.Select([rb, te], state, counts);

        Assert.Equal("George Kittle", slate[0].Player.PlayerName);
    }

    [Fact]
    public void Chris_Rodriguez_Can_Still_Appear_As_Upside()
    {
        var roster = new[]
        {
            Player("Jonathan Taylor", Position.RB),
            Player("James Cook", Position.RB),
            Player("David Montgomery", Position.RB),
            Player("Nico Collins", Position.WR),
            Player("Ladd McConkey", Position.WR),
            Player("Travis Kelce", Position.TE),
            Player("Justin Herbert", Position.QB)
        };
        var counts = Counts(rb: 3, wr: 2, qb: 1, te: 1);
        var state = BuildState(round: 11, roster: roster, counts: counts);
        var wr = Candidate("Wan'Dale Robinson", "WR", fit: 12m, upside: 16m);
        var wr2 = Candidate("Deebo Samuel", "WR", fit: 11.5m, upside: 15m);
        var rod = Candidate("Chris Rodriguez", "RB", fit: 11m, upside: 22m);

        var slate = DraftDecisionEngine.Select([wr, wr2, rod], state, counts);

        Assert.Contains(slate, p =>
            p.Player.PlayerName == "Chris Rodriguez" && p.Role == RecommendationRole.Upside);
    }

    [Fact]
    public void Two_Ir_Stashes_Skip_Kicker_And_Dst()
    {
        var roster = new[]
        {
            Player("Jonathon Brooks", Position.RB),
            Player("Zach Charbonnet", Position.RB)
        };
        var counts = Counts(rb: 2, wr: 0, qb: 0, te: 0);
        var state = BuildState(round: 14, roster: roster, counts: counts);
        Assert.True(state.SkipKickerAndDst);

        var k = Candidate("Justin Tucker", "K", fit: 8m, upside: 10m);
        var wr = Candidate("Wan'Dale Robinson", "WR", fit: 7m, upside: 14m);

        var slate = DraftDecisionEngine.Select([k, wr], state, counts);

        Assert.DoesNotContain(slate, p => p.Player.PositionLabel == "K");
        Assert.Equal("Wan'Dale Robinson", slate[0].Player.PlayerName);
    }

    [Fact]
    public void Kyler_Gets_Late_Qb2_Priority_When_Herbert_Rostered()
    {
        var roster = new[] { Player("Justin Herbert", Position.QB) };
        var counts = Counts(rb: 2, wr: 3, qb: 1, te: 1);
        var state = BuildState(round: 12, roster: roster, counts: counts);
        Assert.True(state.PreferKylerLate);

        var kyler = Candidate("Kyler Murray", "QB", fit: 10m, upside: 18m);
        var randomQb = Candidate("Geno Smith", "QB", fit: 10.5m, upside: 14m);
        var wr = Candidate("Wan'Dale Robinson", "WR", fit: 11m, upside: 16m);

        var slate = DraftDecisionEngine.Select([randomQb, wr, kyler], state, counts);

        Assert.Contains(slate, p => p.Player.PlayerName == "Kyler Murray");
        // PreferKyler should make Kyler competitive vs Geno even with slightly lower fit.
        var kylerPick = slate.First(p => p.Player.PlayerName == "Kyler Murray");
        var genoPick = slate.FirstOrDefault(p => p.Player.PlayerName == "Geno Smith");
        if (genoPick is not null)
        {
            Assert.True(
                slate.ToList().IndexOf(kylerPick) < slate.ToList().IndexOf(genoPick)
                || kylerPick.Role == RecommendationRole.Primary);
        }
    }

    [Fact]
    public void Ladd_Rostered_Downgrades_Quentin_Johnston()
    {
        var roster = new[] { Player("Ladd McConkey", Position.WR) };
        var counts = Counts(rb: 2, wr: 2, qb: 1, te: 1);
        var state = BuildState(round: 11, roster: roster, counts: counts);

        var johnston = Candidate("Quentin Johnston", "WR", fit: 12m, upside: 20m);
        var other = Candidate("Wan'Dale Robinson", "WR", fit: 11.5m, upside: 16m);

        var slate = DraftDecisionEngine.Select([johnston, other], state, counts);

        Assert.Equal("Wan'Dale Robinson", slate[0].Player.PlayerName);
        Assert.Contains(slate[0].WhyBullets.Concat(slate.SelectMany(s => s.WhyBullets)),
            b => b.Contains("Ladd", StringComparison.OrdinalIgnoreCase)
                 || slate.Any(s => s.Player.PlayerName == "Quentin Johnston"
                                   && s.WhyBullets.Any(x => x.Contains("Ladd", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void Downs_And_Addison_Fades_Require_Clear_Value()
    {
        var state = BuildState(round: 8, roster: [], counts: Counts(rb: 2, wr: 2, qb: 1, te: 0));
        var downs = Candidate("Josh Downs", "WR", fit: 12.2m, upside: 18m);
        var solid = Candidate("Chris Godwin", "WR", fit: 12.0m, upside: 16m);

        var slate = DraftDecisionEngine.Select([downs, solid], state, state.PositionalCounts);

        Assert.Equal("Chris Godwin", slate[0].Player.PlayerName);
    }

    [Fact]
    public void LookAhead_Differs_By_Selected_Candidate()
    {
        var roster = new[]
        {
            Player("Jonathan Taylor", Position.RB),
            Player("Nico Collins", Position.WR),
            Player("Justin Herbert", Position.QB)
        };
        var counts = Counts(rb: 1, wr: 1, qb: 1, te: 0);
        var state = BuildState(round: 8, roster: roster, counts: counts);
        var kittle = Candidate("George Kittle", "TE", fit: 12m, upside: 18m);
        var godwin = Candidate("Chris Godwin", "WR", fit: 12m, upside: 17m);

        var slate = DraftDecisionEngine.Select([kittle, godwin], state, counts);
        Assert.True(slate.Count >= 2);

        var a = slate[0].LookAhead.Select(s => s.TargetPosition).ToList();
        var b = slate[1].LookAhead.Select(s => s.TargetPosition).ToList();
        Assert.NotEqual(string.Join(",", a), string.Join(",", b));
    }

    [Fact]
    public void Top_Recommendations_Have_Distinct_Roles()
    {
        var state = BuildState(round: 6, roster: [], counts: EmptyCounts());
        var candidates = new[]
        {
            Candidate("Jonathan Taylor", "RB", 20m, 26m),
            Candidate("Nico Collins", "WR", 18m, 24m),
            Candidate("Chris Rodriguez", "RB", 14m, 22m),
            Candidate("George Kittle", "TE", 15m, 19m)
        };

        var slate = DraftDecisionEngine.Select(candidates, state, state.PositionalCounts);

        Assert.True(slate.Count >= 2);
        Assert.Equal(slate.Count, slate.Select(p => p.Role).Distinct().Count());
    }

    [Fact]
    public void Strategy_State_Rebuilds_From_Roster_Not_Stale()
    {
        var before = BuildState(round: 10, roster: [Player("Travis Kelce", Position.TE)], counts: Counts(te: 1, rb: 2, wr: 2, qb: 1));
        var after = BuildState(
            round: 11,
            roster:
            [
                Player("Travis Kelce", Position.TE),
                Player("Jonathan Taylor", Position.RB),
                Player("James Cook", Position.RB),
                Player("David Montgomery", Position.RB)
            ],
            counts: Counts(te: 1, rb: 3, wr: 2, qb: 1));

        Assert.Equal(1, before.PositionalCounts["TE"]);
        Assert.Equal(3, after.PositionalCounts["RB"]);
        Assert.NotEqual(before.Round, after.Round);
        Assert.Contains("Travis Kelce", after.RosteredPlayerNames);
    }

    private static DraftStrategyState BuildState(
        int round,
        IReadOnlyList<Player> roster,
        IReadOnlyDictionary<string, int> counts) =>
        DraftStrategyState.Build(
            DraftStrategyPlan.DefaultCompanion(),
            round,
            pickNumber: round * 12,
            phase: DraftPhasePolicy.ClassifyFromPick(round * 12, teamCount: 12, totalRounds: 15),
            positionalCounts: counts,
            rosterPlayers: roster);

    private static IReadOnlyDictionary<string, int> EmptyCounts() => Counts(0, 0, 0, 0);

    private static IReadOnlyDictionary<string, int> Counts(int rb = 0, int wr = 0, int qb = 0, int te = 0) =>
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["RB"] = rb,
            ["WR"] = wr,
            ["QB"] = qb,
            ["TE"] = te
        };

    private static DraftDecisionEngine.CandidateInput Candidate(
        string name, string pos, decimal fit, decimal upside)
    {
        var player = Player(name, pos switch
        {
            "QB" => Position.QB,
            "RB" => Position.RB,
            "WR" => Position.WR,
            "TE" => Position.TE,
            _ => Position.K
        });

        return new()
        {
            Player = player,
            BaseRecommendation = new DraftRecommendation
            {
                PlayerId = player.Id,
                PlayerName = name,
                PositionLabel = pos,
                Team = "XX",
                ProjectedPoints = fit,
                ValueOverReplacement = 1m,
                BestPlayerAvailableRank = 1,
                TeamFitRank = 1,
                Confidence = 50,
                Reasoning = "test",
                Factors = []
            },
            TeamFitScore = fit,
            UpsideCeiling = upside,
            Floor = fit - 3m,
            AvailabilityRisk = AvailabilityRisk.Unknown,
            ValueOverReplacement = 1m
        };
    }

    private static Player Player(string name, Position position)
    {
        var parts = name.Split(' ', 2);
        return new Player
        {
            Id = Guid.NewGuid(),
            FullName = name,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : parts[0],
            Position = position,
            Team = "XX",
            Status = PlayerStatus.Active
        };
    }
}
