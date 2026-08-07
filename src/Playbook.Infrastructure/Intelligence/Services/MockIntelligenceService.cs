using Playbook.Application.Intelligence.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// In-memory football intelligence. No fantasy points, rankings, or league settings.
/// </summary>
public sealed class MockIntelligenceService : IIntelligenceService
{
    private static readonly Guid[] PlayerIds =
    [
        Guid.Parse("11111111-1111-1111-1111-111111111101"), // Jayden Daniels
        Guid.Parse("11111111-1111-1111-1111-111111111102"), // Jordan Love
        Guid.Parse("11111111-1111-1111-1111-111111111103"), // Patrick Mahomes
        Guid.Parse("11111111-1111-1111-1111-111111111104"), // Bucky Irving
        Guid.Parse("11111111-1111-1111-1111-111111111105"), // Bijan Robinson
        Guid.Parse("11111111-1111-1111-1111-111111111106"), // Saquon Barkley
        Guid.Parse("11111111-1111-1111-1111-111111111107"), // Jahmyr Gibbs
        Guid.Parse("11111111-1111-1111-1111-111111111108"), // Brian Thomas Jr.
        Guid.Parse("11111111-1111-1111-1111-111111111109"), // Ja'Marr Chase
        Guid.Parse("11111111-1111-1111-1111-111111111110"), // CeeDee Lamb
        Guid.Parse("11111111-1111-1111-1111-111111111111"), // Amon-Ra St. Brown
        Guid.Parse("11111111-1111-1111-1111-111111111112"), // Puka Nacua
        Guid.Parse("11111111-1111-1111-1111-111111111113"), // Travis Kelce
        Guid.Parse("11111111-1111-1111-1111-111111111114"), // Brock Bowers
        Guid.Parse("11111111-1111-1111-1111-111111111115")  // Trey McBride
    ];

    private readonly IReadOnlyList<IntelligenceFact> _facts;

    public MockIntelligenceService()
    {
        _facts = BuildFacts();
    }

    public IReadOnlyList<IntelligenceFact> GetAllFacts() => _facts;

    public IReadOnlyList<IntelligenceFact> GetTopFacts(int count = 8) =>
        _facts
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.Confidence)
            .ThenByDescending(f => f.Created)
            .Take(Math.Max(0, count))
            .ToList();

    public IReadOnlyList<IntelligenceFact> GetFactsForPlayer(Guid playerId) =>
        _facts
            .Where(f => f.RelatedPlayerId == playerId)
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.Confidence)
            .ToList();

    public PlayerIntelligence? GetPlayerIntelligence(Guid playerId)
    {
        var facts = GetFactsForPlayer(playerId);
        if (facts.Count == 0)
        {
            return null;
        }

        var confidence = (int)Math.Round(facts.Average(f => f.Confidence));
        var trend = InferTrend(facts);

        return new PlayerIntelligence
        {
            PlayerId = playerId,
            OverallConfidence = confidence,
            Facts = facts,
            TrendSummary = SummarizeTrend(facts, trend),
            RiskSummary = SummarizeRisk(facts),
            OpportunitySummary = SummarizeOpportunity(facts),
            LastUpdated = facts.Max(f => f.Created),
            TrendDirection = trend
        };
    }

    private static TrendDirection InferTrend(IReadOnlyList<IntelligenceFact> facts)
    {
        var usageUp = facts.Count(f =>
            f.Category is IntelligenceCategory.Usage or IntelligenceCategory.Opportunity or IntelligenceCategory.Efficiency
            && f.Importance >= IntelligenceImportance.Medium);

        var risk = facts.Count(f =>
            f.Category is IntelligenceCategory.Injury or IntelligenceCategory.Weather
            && f.Importance >= IntelligenceImportance.High);

        if (usageUp >= risk + 1)
        {
            return TrendDirection.Up;
        }

        if (risk > usageUp)
        {
            return TrendDirection.Down;
        }

        return TrendDirection.Flat;
    }

    private static string SummarizeTrend(IReadOnlyList<IntelligenceFact> facts, TrendDirection trend)
    {
        var usage = facts.FirstOrDefault(f => f.Category == IntelligenceCategory.Usage);
        return trend switch
        {
            TrendDirection.Up => usage?.Title ?? "Positive usage and opportunity signals are accumulating.",
            TrendDirection.Down => "Caution signals outweigh recent opportunity indicators.",
            _ => "Mixed signals — role appears stable week to week."
        };
    }

    private static string SummarizeRisk(IReadOnlyList<IntelligenceFact> facts)
    {
        var risk = facts
            .Where(f => f.Category is IntelligenceCategory.Injury or IntelligenceCategory.Weather or IntelligenceCategory.Situation)
            .OrderByDescending(f => f.Importance)
            .FirstOrDefault();

        return risk?.Description ?? "No elevated football risk signals in the current window.";
    }

    private static string SummarizeOpportunity(IReadOnlyList<IntelligenceFact> facts)
    {
        var opp = facts
            .Where(f => f.Category is IntelligenceCategory.Opportunity or IntelligenceCategory.Matchup or IntelligenceCategory.Scheme)
            .OrderByDescending(f => f.Importance)
            .FirstOrDefault();

        return opp?.Description ?? "No standout opportunity signal beyond baseline role.";
    }

    private static IReadOnlyList<IntelligenceFact> BuildFacts()
    {
        var now = DateTimeOffset.UtcNow;
        var facts = new List<IntelligenceFact>(80);
        var n = 0;

        void Add(
            string title,
            string description,
            IntelligenceCategory category,
            int confidence,
            IntelligenceImportance importance,
            IntelligenceSource source,
            Guid? playerId,
            string? teamId,
            string[] evidence,
            string[] tags,
            int hoursAgo,
            int? expiresHours = 168)
        {
            n++;
            facts.Add(new IntelligenceFact
            {
                Id = Guid.Parse($"a1a1a1a1-a1a1-a1a1-a1a1-{n:D12}"),
                Title = title,
                Description = description,
                Category = category,
                Confidence = confidence,
                Importance = importance,
                Source = source,
                Created = now.AddHours(-hoursAgo),
                Expires = expiresHours is int h ? now.AddHours(h - hoursAgo) : null,
                RelatedPlayerId = playerId,
                RelatedTeamId = teamId,
                RelatedGameId = null,
                SupportingEvidence = evidence,
                Tags = tags
            });
        }

        // --- Player-linked facts (diverse categories) ---
        Add("Increasing snap share",
            "Route participation and offensive snaps are climbing versus the prior three-week baseline.",
            IntelligenceCategory.Usage, 86, IntelligenceImportance.High, IntelligenceSource.Tracking,
            PlayerIds[0], "WAS",
            ["Snap share +9% vs prior 3 weeks", "Route participation 92% last outing"],
            ["snaps", "usage"], 6);

        Add("Red zone usage increasing",
            "Inside-the-10 touches and designed looks are rising after a quiet early stretch.",
            IntelligenceCategory.Opportunity, 81, IntelligenceImportance.High, IntelligenceSource.Charting,
            PlayerIds[0], "WAS",
            ["3 RZ touches last 2 games", "Designed keepers near goal line"],
            ["red-zone", "opportunity"], 10);

        Add("Explosive play trend",
            "Chunk gains of 20+ yards are occurring at a higher rate than team average.",
            IntelligenceCategory.Efficiency, 78, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[0], "WAS",
            ["2 plays of 25+ yards last game", "EPA on designed keepers elevated"],
            ["explosiveness"], 14);

        Add("Offensive pace increasing",
            "Seconds-per-play is trending down, creating more total plays for skill talent.",
            IntelligenceCategory.Scheme, 74, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[1], "GB",
            ["Pace rank improved from 22nd to 11th", "No-huddle usage up in 2H"],
            ["pace", "scheme"], 8);

        Add("Coach praise",
            "Primary skill players drew public praise for practice tempo and assignment detail.",
            IntelligenceCategory.Coaching, 68, IntelligenceImportance.Medium, IntelligenceSource.Coaching,
            PlayerIds[1], "GB",
            ["Midweek presser highlighted trust", "Reps with starters unchanged"],
            ["coaching", "trust"], 20);

        Add("Positive matchup",
            "Opponent allows above-average efficiency to this position group over the last month.",
            IntelligenceCategory.Matchup, 83, IntelligenceImportance.High, IntelligenceSource.Historical,
            PlayerIds[1], "GB",
            ["Opp allows 7.8 YPA to QBs", "Cover-3 rate creates intermediate windows"],
            ["matchup"], 4);

        Add("Vegas total rising",
            "Consensus game total has moved upward after early line open, implying more scoring environment.",
            IntelligenceCategory.Market, 79, IntelligenceImportance.High, IntelligenceSource.BettingMarket,
            PlayerIds[2], "KC",
            ["Total opened 46.5 → 49.0", "Public money leaning overs"],
            ["vegas", "environment"], 3);

        Add("Projected shootout environment",
            "Both offenses are projected to sustain drives; expected play volume is elevated.",
            IntelligenceCategory.Situation, 77, IntelligenceImportance.High, IntelligenceSource.Historical,
            PlayerIds[2], "KC",
            ["Combined implied total 49+", "Both teams top-10 in plays/game"],
            ["shootout", "pace"], 5);

        Add("Returning from injury",
            "Cleared for full practice after limited sessions; expected to resume primary role.",
            IntelligenceCategory.Injury, 72, IntelligenceImportance.Critical, IntelligenceSource.InjuryReport,
            PlayerIds[3], "TB",
            ["Full participant Wednesday–Friday", "No game-day designation expected"],
            ["injury", "return"], 2);

        Add("Increasing snap share",
            "Backfield snaps continue to consolidate toward the lead early-down role.",
            IntelligenceCategory.Usage, 88, IntelligenceImportance.Critical, IntelligenceSource.Tracking,
            PlayerIds[3], "TB",
            ["68% early-down snaps", "Pass-pro reps expanding"],
            ["snaps", "rb"], 7);

        Add("Offensive line healthier",
            "Starting OL projected intact after recent absences, improving gap integrity.",
            IntelligenceCategory.Situation, 75, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            PlayerIds[4], "ATL",
            ["LT and LG full practice", "Prior 2 games missing a starter"],
            ["oline", "health"], 9);

        Add("Coach historically increases RB usage after losses",
            "Historical play-call tendencies show elevated early-down RB volume following defeats.",
            IntelligenceCategory.Coaching, 70, IntelligenceImportance.Medium, IntelligenceSource.Historical,
            PlayerIds[4], "ATL",
            ["+4.2 RB carries after losses (3yr)", "Team lost last week"],
            ["coaching", "script"], 11);

        Add("Heavy rain expected",
            "Game-time forecast calls for steady rain, historically suppressing deep passing volume.",
            IntelligenceCategory.Weather, 84, IntelligenceImportance.High, IntelligenceSource.Weather,
            PlayerIds[5], "PHI",
            ["80% rain probability at kickoff", "Wind 12–15 mph"],
            ["weather", "rain"], 1);

        Add("Weather concern",
            "Gusty conditions may mute perimeter throws and favor interior / ground concepts.",
            IntelligenceCategory.Weather, 76, IntelligenceImportance.Medium, IntelligenceSource.Weather,
            PlayerIds[5], "PHI",
            ["Sustained winds above 15 mph", "Prior similar games: -18% deep attempts"],
            ["weather", "wind"], 1);

        Add("Red zone usage increasing",
            "Goal-line and inside-5 touches are clustering with the primary back.",
            IntelligenceCategory.Opportunity, 85, IntelligenceImportance.High, IntelligenceSource.Charting,
            PlayerIds[6], "DET",
            ["4 carries inside 5 last 2 games", "No committee split on GL"],
            ["red-zone"], 12);

        Add("Explosive play trend",
            "Broken-tackle rate and yards after contact are above positional peers recently.",
            IntelligenceCategory.Efficiency, 80, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[6], "DET",
            ["YAC/attempt +0.8 vs season", "Missed tackles forced: 6 last game"],
            ["yac", "explosiveness"], 15);

        Add("Positive matchup",
            "Opponent struggles containing vertical threats from the boundary and slot.",
            IntelligenceCategory.Matchup, 87, IntelligenceImportance.Critical, IntelligenceSource.Historical,
            PlayerIds[7], "JAX",
            ["Opp allows 11.2 YPT to WRs", "Slot CB ranked bottom-8 in coverage grade"],
            ["matchup", "wr"], 4);

        Add("Target share rising",
            "Share of team targets has increased for three straight contests.",
            IntelligenceCategory.Usage, 89, IntelligenceImportance.Critical, IntelligenceSource.Tracking,
            PlayerIds[7], "JAX",
            ["Target share 28% → 31% → 34%", "First-read rate elevated"],
            ["targets", "usage"], 6);

        Add("Opponent struggles against slot receivers",
            "Defensive alignment leaves soft coverage versus slot separators on early downs.",
            IntelligenceCategory.Matchup, 82, IntelligenceImportance.High, IntelligenceSource.Film,
            PlayerIds[8], "CIN",
            ["Slot separation +0.4 yards", "Cover-2 rate creates intermediate voids"],
            ["slot", "matchup"], 8);

        Add("Offensive pace increasing",
            "No-huddle sequences are extending, lifting expected pass volume.",
            IntelligenceCategory.Scheme, 73, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[8], "CIN",
            ["Plays/game +3.1 vs season", "2-minute drill efficiency up"],
            ["pace"], 13);

        Add("Returning from injury",
            "Questionable tag removed after consecutive full practices.",
            IntelligenceCategory.Injury, 69, IntelligenceImportance.High, IntelligenceSource.InjuryReport,
            PlayerIds[9], "DAL",
            ["Full Friday practice", "Prior limited designation cleared"],
            ["injury"], 3);

        Add("Depth chart consolidation",
            "Two-deep WR roles tightened; primary options seeing stable starter snaps.",
            IntelligenceCategory.Opportunity, 71, IntelligenceImportance.Medium, IntelligenceSource.DepthChart,
            PlayerIds[9], "DAL",
            ["WR3 snap share compressed", "Starter routes unchanged"],
            ["depth-chart"], 16);

        Add("Increasing snap share",
            "Slot alignment snaps remain elite with expanded motion usage.",
            IntelligenceCategory.Usage, 90, IntelligenceImportance.High, IntelligenceSource.Tracking,
            PlayerIds[10], "DET",
            ["Route % 96 last 3", "Motion on 38% of snaps"],
            ["slot", "usage"], 5);

        Add("Coach praise",
            "Weekly availability and route detail highlighted as model for the room.",
            IntelligenceCategory.Coaching, 66, IntelligenceImportance.Low, IntelligenceSource.Coaching,
            PlayerIds[10], "DET",
            ["Coach quote: 'first look on third down'", "Unchanged starter role"],
            ["coaching"], 22);

        Add("Positive matchup",
            "Corner group allows yards after catch on crossing concepts.",
            IntelligenceCategory.Matchup, 80, IntelligenceImportance.High, IntelligenceSource.Historical,
            PlayerIds[11], "LAR",
            ["YAC allowed 5.9 to WRs", "Zone rate 62%"],
            ["matchup", "yac"], 7);

        Add("Explosive play trend",
            "Average depth of target remains elevated with chunk completion rate rising.",
            IntelligenceCategory.Efficiency, 77, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[11], "LAR",
            ["aDOT 12.4", "20+ yard catches: 3 last 2"],
            ["adot", "explosiveness"], 9);

        Add("Red zone usage increasing",
            "TE looks near the goal line are concentrating after empty packages.",
            IntelligenceCategory.Opportunity, 84, IntelligenceImportance.High, IntelligenceSource.Charting,
            PlayerIds[12], "KC",
            ["3 RZ targets last game", "Inline vs flexed mix balanced"],
            ["te", "red-zone"], 6);

        Add("Scheme emphasis on TE seam",
            "Play designs continue to stress intermediate seams against single-high looks.",
            IntelligenceCategory.Scheme, 75, IntelligenceImportance.Medium, IntelligenceSource.Film,
            PlayerIds[12], "KC",
            ["Seam concepts 8x last game", "Single-high rate for opp: 54%"],
            ["scheme", "te"], 11);

        Add("Increasing snap share",
            "Rookie TE route participation remains among the highest at the position.",
            IntelligenceCategory.Usage, 88, IntelligenceImportance.Critical, IntelligenceSource.Tracking,
            PlayerIds[13], "LV",
            ["Route % 89", "Inline + flexed versatility"],
            ["te", "usage"], 4);

        Add("Positive matchup",
            "Opponent allows elevated TE reception volume on early downs.",
            IntelligenceCategory.Matchup, 81, IntelligenceImportance.High, IntelligenceSource.Historical,
            PlayerIds[13], "LV",
            ["Opp TE targets/game 8.4", "Soft middle coverage"],
            ["matchup", "te"], 5);

        Add("Target share rising",
            "Team pass game is funneling more looks to the primary TE each week.",
            IntelligenceCategory.Usage, 86, IntelligenceImportance.High, IntelligenceSource.Tracking,
            PlayerIds[14], "ARI",
            ["TE target share 24%", "First downs via TE: 6 last game"],
            ["targets", "te"], 8);

        Add("Offensive line healthier",
            "Interior OL returns improve protection time for intermediate TE concepts.",
            IntelligenceCategory.Situation, 70, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            PlayerIds[14], "ARI",
            ["C and RG full practice", "Prior pressures from interior"],
            ["oline"], 10);

        // --- Additional player facts to reach ~75 ---
        Add("Pass protection grade improving",
            "Pressure rate when keeping the pocket clean has dropped in consecutive weeks.",
            IntelligenceCategory.Efficiency, 74, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[0], "WAS",
            ["Pressure rate -6%", "Time to throw stable"],
            ["protection"], 18);

        Add("Two-minute drill involvement",
            "Late-drive designed runs and checkdowns are concentrating with the lead back.",
            IntelligenceCategory.Opportunity, 73, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[3], "TB",
            ["4 touches in final 4:00", "No committee rotation late"],
            ["two-minute"], 17);

        Add("Motion usage expanding",
            "Pre-snap motion into the formation is creating leverage for boundary releases.",
            IntelligenceCategory.Scheme, 72, IntelligenceImportance.Medium, IntelligenceSource.Film,
            PlayerIds[7], "JAX",
            ["Motion rate 41%", "Separation +0.3y on motion snaps"],
            ["motion", "scheme"], 12);

        Add("Third-down trust rising",
            "Conversion-down targets and snaps are clustering with one primary option.",
            IntelligenceCategory.Usage, 85, IntelligenceImportance.High, IntelligenceSource.Tracking,
            PlayerIds[10], "DET",
            ["3rd-down target share 36%", "Slot alignment preferred"],
            ["third-down"], 9);

        Add("After-contact efficiency",
            "Yards after contact per rush remain above the backfield average.",
            IntelligenceCategory.Efficiency, 79, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[5], "PHI",
            ["YCO/att 3.1", "Broken tackles 4 last game"],
            ["yac"], 14);

        Add("Screen game emphasis",
            "Call sheet shows elevated screen volume to skill players this week.",
            IntelligenceCategory.Scheme, 68, IntelligenceImportance.Low, IntelligenceSource.Coaching,
            PlayerIds[6], "DET",
            ["Practice script featured screens", "Prior week: 3 screen looks"],
            ["screens"], 19);

        Add("Corner matchup favorable",
            "Projected shadow corner has allowed separation on vertical stems recently.",
            IntelligenceCategory.Matchup, 83, IntelligenceImportance.High, IntelligenceSource.Film,
            PlayerIds[8], "CIN",
            ["Opp CB allowed 3 catches 15+ last game", "Press rate down"],
            ["cb", "matchup"], 3);

        Add("Inline blocking load manageable",
            "Pass-route rate remains high despite occasional inline sets.",
            IntelligenceCategory.Usage, 76, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[13], "LV",
            ["Route rate 84% when inline", "Blocking snaps not suppressing targets"],
            ["te", "routes"], 15);

        Add("Short-area separation trending up",
            "Release wins versus press are improving on early downs.",
            IntelligenceCategory.Efficiency, 78, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[11], "LAR",
            ["Separation 0–10 yards +0.25", "Press win rate 58%"],
            ["separation"], 11);

        Add("Goal-line package unchanged",
            "Short-yardage personnel continues to feature the same primary ball carrier.",
            IntelligenceCategory.Opportunity, 82, IntelligenceImportance.High, IntelligenceSource.DepthChart,
            PlayerIds[4], "ATL",
            ["100% of GL carries last 3", "No fullback package change"],
            ["goal-line"], 7);

        Add("Practice intensity note",
            "Full-speed team periods included primary skill group without limitations.",
            IntelligenceCategory.Injury, 71, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            PlayerIds[2], "KC",
            ["No walkthrough-only tags", "Starter reps maintained"],
            ["practice"], 2);

        Add("Spread concepts favored",
            "Empty and 11-personnel rates are rising, expanding route inventories.",
            IntelligenceCategory.Scheme, 74, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[1], "GB",
            ["11 personnel 68%", "Empty 12% of snaps"],
            ["personnel"], 16);

        Add("Middle-field void expected",
            "Opponent single-high tendencies leave seams available for TE/WR crossers.",
            IntelligenceCategory.Matchup, 80, IntelligenceImportance.High, IntelligenceSource.Historical,
            PlayerIds[12], "KC",
            ["Single-high 57%", "Seam EPA allowed elevated"],
            ["coverage", "seam"], 5);

        Add("Early-down passing uptick",
            "Early-down pass rate climbed, increasing opportunity for perimeter targets.",
            IntelligenceCategory.Situation, 77, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[9], "DAL",
            ["1st-down pass rate +8%", "Play-action rate stable"],
            ["early-down"], 13);

        Add("Backfield pass-game role expanding",
            "Checkdowns and designed overs are landing with the lead back more often.",
            IntelligenceCategory.Usage, 81, IntelligenceImportance.High, IntelligenceSource.Tracking,
            PlayerIds[6], "DET",
            ["Routes/game 4.2 → 6.1", "Target share on passes +4%"],
            ["pass-catching", "rb"], 8);

        Add("Interior pressure concern for opponent",
            "Opp OL injuries raise sack/pressure odds, potentially lifting scramble and checkdown volume.",
            IntelligenceCategory.Situation, 69, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            PlayerIds[0], "WAS",
            ["Opp LG questionable", "Prior games with backup: +pressure"],
            ["pressure", "situation"], 4);

        Add("Slot CB mismatch",
            "Projected slot defender ranks poorly versus quick separators.",
            IntelligenceCategory.Matchup, 86, IntelligenceImportance.Critical, IntelligenceSource.Film,
            PlayerIds[10], "DET",
            ["Slot CB coverage grade bottom-10", "Quick game rate high for DET"],
            ["slot", "mismatch"], 3);

        Add("Deep shot rate elevated",
            "Team is taking more shots beyond 20 yards when protection holds.",
            IntelligenceCategory.Scheme, 72, IntelligenceImportance.Medium, IntelligenceSource.Charting,
            PlayerIds[7], "JAX",
            ["20+ aDOT attempts 6 last game", "Protection clean on 4"],
            ["deep", "scheme"], 10);

        Add("Contested-catch wins",
            "High-point and body-control receptions converting at an above-average rate.",
            IntelligenceCategory.Efficiency, 75, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[8], "CIN",
            ["Contested catch rate 62%", "3 contested targets last game"],
            ["contested"], 12);

        Add("Short-yardage reliability",
            "Conversion rate on 3rd-and-1 / 4th-and-1 remains strong with lead back.",
            IntelligenceCategory.Opportunity, 83, IntelligenceImportance.High, IntelligenceSource.Historical,
            PlayerIds[5], "PHI",
            ["8/9 conversions short yardage", "No wildcat package change"],
            ["short-yardage"], 18);

        Add("Bootleg emphasis",
            "Play-action boot concepts are expanding look volume for trailing TEs/WRs.",
            IntelligenceCategory.Scheme, 70, IntelligenceImportance.Medium, IntelligenceSource.Film,
            PlayerIds[14], "ARI",
            ["Boot looks 5x last game", "TE as primary on 3"],
            ["boot", "play-action"], 14);

        Add("Practice report: limited earlier, upgraded",
            "Midweek limited tag upgraded to full; availability outlook improved.",
            IntelligenceCategory.Injury, 67, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            PlayerIds[11], "LAR",
            ["Wed limited → Fri full", "No setback reported"],
            ["injury", "upgrade"], 2);

        Add("Run-fit discipline slipping for opponent",
            "Opp linebackers over-pursuing, creating cutback lanes.",
            IntelligenceCategory.Matchup, 78, IntelligenceImportance.Medium, IntelligenceSource.Film,
            PlayerIds[4], "ATL",
            ["Missed tackles 9 last game", "Cutback EPA allowed high"],
            ["run-fit"], 6);

        Add("Hot-read involvement",
            "Sight adjustments and hot throws are finding the primary underneath option.",
            IntelligenceCategory.Usage, 74, IntelligenceImportance.Medium, IntelligenceSource.Tracking,
            PlayerIds[13], "LV",
            ["Hot targets 3 last game", "Pressure-to-hot conversion solid"],
            ["hot", "protection"], 9);

        Add("Tempo after scores",
            "Offense is hurrying pace after scoring drives, adding extra series potential.",
            IntelligenceCategory.Situation, 71, IntelligenceImportance.Low, IntelligenceSource.Charting,
            PlayerIds[2], "KC",
            ["Post-score plays within 25s", "Extra series in 2 of last 3"],
            ["tempo"], 20);

        Add("Blocking scheme favors outside zone",
            "Outside-zone rate is up, aligning with back's vision profile.",
            IntelligenceCategory.Scheme, 76, IntelligenceImportance.Medium, IntelligenceSource.Coaching,
            PlayerIds[3], "TB",
            ["Outside zone 44% of rushes", "Success rate 48%"],
            ["outside-zone"], 11);

        Add("Coverage tells favoring digs",
            "Opp safeties rotating late, opening dig/cross windows at 12–15 yards.",
            IntelligenceCategory.Matchup, 79, IntelligenceImportance.High, IntelligenceSource.Film,
            PlayerIds[9], "DAL",
            ["Late safety rotation 31%", "Dig concept EPA positive"],
            ["coverage", "dig"], 5);

        // --- Team / environment facts (no player or broad) ---
        Add("Heavy rain expected across afternoon window",
            "Regional forecast supports wet field conditions for outdoor early games.",
            IntelligenceCategory.Weather, 82, IntelligenceImportance.High, IntelligenceSource.Weather,
            null, null,
            ["NWS: steady rain 1–5pm ET", "Field drainage notes mixed"],
            ["weather", "slate"], 1);

        Add("League-wide pace ticking up",
            "Average plays per game across the league is rising week over week.",
            IntelligenceCategory.Situation, 65, IntelligenceImportance.Low, IntelligenceSource.Historical,
            null, null,
            ["League plays/game +1.4", "No-huddle rate +2%"],
            ["pace", "league"], 24);

        Add("Wind advisory for primetime site",
            "Sustained winds may suppress deep throwing windows in the featured night game.",
            IntelligenceCategory.Weather, 80, IntelligenceImportance.High, IntelligenceSource.Weather,
            null, "PHI",
            ["Wind 18–22 mph gusts", "Similar prior: deep att -15%"],
            ["weather", "primetime"], 2);

        Add("Injury report volume elevated league-wide",
            "Thursday reports show more limited tags than the seasonal average.",
            IntelligenceCategory.Injury, 64, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            null, null,
            ["Limited tags +12% vs avg Thursday", "Several OL designations"],
            ["injury-report"], 8);

        Add("Betting markets implying higher scoring",
            "Average totals across the slate have drifted higher since open.",
            IntelligenceCategory.Market, 78, IntelligenceImportance.High, IntelligenceSource.BettingMarket,
            null, null,
            ["Avg total +1.1 since open", "Overs attracting tickets"],
            ["vegas", "slate"], 3);

        Add("Defensive backs dealing with absences",
            "Multiple clubs listing starting CBs as questionable, creating mismatch windows.",
            IntelligenceCategory.Situation, 73, IntelligenceImportance.Medium, IntelligenceSource.InjuryReport,
            null, null,
            ["6 starting CBs questionable", "Nickel depth thin on 2 clubs"],
            ["cb", "injury"], 4);

        Add("Coach speak emphasizes establishing run",
            "Multiple midweek comments stress early-down run commitment after recent losses.",
            IntelligenceCategory.Coaching, 62, IntelligenceImportance.Low, IntelligenceSource.Coaching,
            null, null,
            ["3 coaches cited run emphasis", "Historically followed ~60% of time"],
            ["coaching", "script"], 21);

        Add("Turf vs grass split this week",
            "Surface mix may influence cut-heavy route trees and certain RB styles.",
            IntelligenceCategory.Situation, 60, IntelligenceImportance.Low, IntelligenceSource.Historical,
            null, null,
            ["8 outdoor grass", "5 turf domes"],
            ["surface"], 30);

        Add("Short week recovery flag",
            "Thursday participants face compressed recovery; snap management possible.",
            IntelligenceCategory.Situation, 70, IntelligenceImportance.Medium, IntelligenceSource.Historical,
            null, null,
            ["TNF participants listed", "Prior TNF snap dips common for skill"],
            ["short-week"], 26);

        Add("Special teams return involvement changing",
            "One club shifted return duties, slightly altering conditioning load for a WR.",
            IntelligenceCategory.Opportunity, 58, IntelligenceImportance.Low, IntelligenceSource.DepthChart,
            PlayerIds[7], "JAX",
            ["PR duties reassigned midweek", "Offensive snap share unchanged"],
            ["returns"], 15);

        // Ensure we have at least 75
        if (facts.Count < 75)
        {
            var fillers = new (string title, string desc, IntelligenceCategory cat, IntelligenceSource src, Guid? pid, string? team)[]
            {
                ("Pocket movement improving", "Designed rollouts and spontaneous escapes are yielding positive EPA.", IntelligenceCategory.Efficiency, IntelligenceSource.Tracking, PlayerIds[0], "WAS"),
                ("Stack release creating free access", "Bunch/stack looks are freeing the primary separator at the snap.", IntelligenceCategory.Scheme, IntelligenceSource.Film, PlayerIds[8], "CIN"),
                ("Backfield alignment predictability down", "Offset and pistol mixes are reducing early defensive keys.", IntelligenceCategory.Scheme, IntelligenceSource.Coaching, PlayerIds[5], "PHI"),
                ("Catch radius converting 50/50s", "Extended catches outside the numbers remain a reliable outlet.", IntelligenceCategory.Efficiency, IntelligenceSource.Tracking, PlayerIds[11], "LAR"),
                ("TE chip-then-release working", "Chip releases are still producing open windows after chip duties.", IntelligenceCategory.Usage, IntelligenceSource.Charting, PlayerIds[14], "ARI"),
                ("Opponent nickel personnel vulnerable", "Light boxes against 11 personnel invite early-down runs.", IntelligenceCategory.Matchup, IntelligenceSource.Historical, PlayerIds[4], "ATL"),
                ("Play-action hit rate rising", "Play-action completion rate and EPA are above season norms.", IntelligenceCategory.Efficiency, IntelligenceSource.Tracking, PlayerIds[1], "GB"),
                ("Boundary vs slot mix optimizing", "Alignment diversity is keeping coverage assignments unclear.", IntelligenceCategory.Scheme, IntelligenceSource.Film, PlayerIds[9], "DAL"),
                ("Goal-to-go pass lean", "Inside the 10, pass rate ticked up featuring primary WRs/TEs.", IntelligenceCategory.Opportunity, IntelligenceSource.Charting, PlayerIds[12], "KC"),
                ("Scramble drill production", "Off-schedule throws are finding progressive reads downfield.", IntelligenceCategory.Efficiency, IntelligenceSource.Tracking, PlayerIds[2], "KC"),
                ("Blitz weakness vs TE flats", "Opp blitz leaves flats open; TE checkdowns converting.", IntelligenceCategory.Matchup, IntelligenceSource.Film, PlayerIds[13], "LV"),
                ("Early script featuring perimeter", "Opening 15 plays emphasize outside runs and quick outs.", IntelligenceCategory.Coaching, IntelligenceSource.Coaching, PlayerIds[3], "TB"),
                ("Secondary rotation instability", "Opp mixing coverages pre-snap, occasionally late to landmarks.", IntelligenceCategory.Matchup, IntelligenceSource.Film, PlayerIds[10], "DET"),
                ("Trash-time volume potential", "Game script models allow extended garbage-time pass volume.", IntelligenceCategory.Situation, IntelligenceSource.Historical, PlayerIds[7], "JAX"),
                ("Full go on walkthroughs", "No soft practices; primary players completing team periods.", IntelligenceCategory.Injury, IntelligenceSource.InjuryReport, PlayerIds[6], "DET"),
            };

            foreach (var f in fillers)
            {
                if (facts.Count >= 75)
                {
                    break;
                }

                Add(f.title, f.desc, f.cat, 65 + (facts.Count % 20), IntelligenceImportance.Medium, f.src,
                    f.pid, f.team,
                    ["Mock charting sample", "Mock tracking corroboration"],
                    ["generated"], 10 + facts.Count);
            }
        }

        return facts;
    }
}
