using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Builds a weekly matchup game plan from league context, projections, and team intelligence.
/// Opponent pairing uses a deterministic derived demo when a live H2H feed is unavailable.
/// </summary>
public sealed class WeeklyMatchupGamePlanService : IWeeklyMatchupGamePlanService
{
    private readonly ILeagueState _leagueState;
    private readonly IPlayerService _players;
    private readonly IProjectionService _projections;
    private readonly IFantasyTeamIntelligenceService _teamIntel;
    private readonly IPlayerIntelligenceAssessmentService _assessments;
    private readonly object _gate = new();

    private WeeklyMatchupGamePlan? _cached;
    private PersonalizedAnalysisContext _cachedContext;
    private int? _cachedOpponentRosterId;

    public WeeklyMatchupGamePlanService(
        ILeagueState leagueState,
        IPlayerService players,
        IProjectionService projections,
        IFantasyTeamIntelligenceService teamIntel,
        IPlayerIntelligenceAssessmentService assessments)
    {
        _leagueState = leagueState;
        _players = players;
        _projections = projections;
        _teamIntel = teamIntel;
        _assessments = assessments;
        _leagueState.Changed += OnLeagueContextChanged;
    }

    public WeeklyMatchupGamePlan GetPlan()
    {
        var context = PersonalizedAnalysisContext.FromState(_leagueState);
        var opponent = ResolveOpponent(context);
        if (_cached is not null &&
            context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
            context.ScoringType == _cachedContext.ScoringType &&
            context.Week == _cachedContext.Week &&
            _cachedOpponentRosterId == opponent?.RosterId)
        {
            return _cached;
        }

        lock (_gate)
        {
            context = PersonalizedAnalysisContext.FromState(_leagueState);
            opponent = ResolveOpponent(context);
            if (_cached is not null &&
                context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
                context.ScoringType == _cachedContext.ScoringType &&
                context.Week == _cachedContext.Week &&
                _cachedOpponentRosterId == opponent?.RosterId)
            {
                return _cached;
            }

            _cached = BuildPlan(context, opponent);
            _cachedContext = context;
            _cachedOpponentRosterId = opponent?.RosterId;
            return _cached;
        }
    }

    private void OnLeagueContextChanged()
    {
        lock (_gate)
        {
            _cached = null;
            _cachedContext = default;
            _cachedOpponentRosterId = null;
        }
    }

    private FantasyTeam? ResolveOpponent(PersonalizedAnalysisContext context)
    {
        var mine = _leagueState.CurrentUserTeam;
        if (mine is null || context.LeagueId is null)
        {
            return null;
        }

        var candidates = _leagueState.GetCurrentTeams()
            .Where(t => t.RosterId != mine.RosterId && t.PlayerIds.Count > 0)
            .OrderBy(t => t.RosterId)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // Deterministic week rotation — no fabricated H2H feed.
        var index = Math.Abs(context.Week - 1) % candidates.Count;
        return candidates[index];
    }

    private WeeklyMatchupGamePlan BuildPlan(PersonalizedAnalysisContext context, FantasyTeam? opponent)
    {
        var now = DateTimeOffset.UtcNow;
        var league = _leagueState.CurrentLeague;
        var mine = _leagueState.CurrentUserTeam;

        if (league is null)
        {
            return Empty(context, "Select or connect a league to open this week's game plan.", now);
        }

        if (!context.IsSetupComplete || mine is null)
        {
            return Empty(context, "Pick your owned team to generate a weekly matchup game plan.", now, league);
        }

        if (mine.PlayerIds.Count == 0)
        {
            return Empty(context, "Your roster has no players loaded yet.", now, league, mine);
        }

        if (opponent is null)
        {
            return Empty(
                context,
                "No opponent roster is available in this league yet. Connect a live league or wait for more demo teams.",
                now,
                league,
                mine);
        }

        var myReport = _teamIntel.GetReport();
        var mySide = BuildSide(mine, preferStarters: true);
        var oppSide = BuildSide(opponent, preferStarters: true);

        var myScore = SumProjected(mySide.Lineup);
        var oppScore = SumProjected(oppSide.Lineup);
        var diff = myScore is null || oppScore is null ? (decimal?)null : myScore - oppScore;

        var matchupConfidence = DeriveMatchupConfidence(mySide, oppSide, myReport);
        var assessment = DeriveAssessment(diff, matchupConfidence);
        var volatility = DeriveVolatility(mySide, oppSide);
        var swings = BuildSwings(mySide, oppSide);
        var risks = BuildRisks(mySide, myReport);
        var advantages = BuildAdvantages(mySide, oppSide, myReport, diff);
        var lineupImpact = BuildLineupImpact(myReport.StartSit, myScore, oppScore);
        var scout = BuildOpponentScout(opponent, oppSide);

        var unavailable = new List<string>();
        unavailable.AddRange(mySide.Unavailable);
        unavailable.AddRange(oppSide.Unavailable);
        if (mySide.Lineup.Count(p => p.Projection is null) > 0)
        {
            unavailable.Add("Some of your lineup projections are unavailable");
        }

        if (oppSide.Lineup.Count(p => p.Projection is null) > 0)
        {
            unavailable.Add("Some opponent lineup projections are unavailable");
        }

        unavailable.Add("Opponent paired from league rosters (live head-to-head feed unavailable)");

        return new WeeklyMatchupGamePlan
        {
            LeagueId = league.Id,
            SelectedRosterId = mine.RosterId,
            OpponentRosterId = opponent.RosterId,
            LeagueName = league.Name,
            Week = league.CurrentWeek,
            ScoringLabel = FormatScoring(league.ScoringType),
            IsSetupComplete = true,
            HasMatchup = true,
            MyTeamName = TeamLabel(mine),
            OpponentTeamName = TeamLabel(opponent),
            OpponentSource = MatchupOpponentSource.DerivedDemo,
            MyProjectedScore = myScore,
            OpponentProjectedScore = oppScore,
            ProjectionDifference = diff,
            Assessment = assessment,
            AssessmentLabel = assessment switch
            {
                MatchupAssessment.Favorable => "Favorable",
                MatchupAssessment.Competitive => "Competitive",
                MatchupAssessment.Challenging => "Challenging",
                _ => "Unknown"
            },
            AssessmentSummary = BuildAssessmentSummary(assessment, diff, matchupConfidence, volatility),
            MatchupConfidence = matchupConfidence,
            ConfidenceNote = matchupConfidence switch
            {
                >= 70 => "Matchup confidence is supported by broad projection coverage.",
                >= 45 => "Matchup confidence is moderate — some signals are thin.",
                _ => "Matchup confidence is low — treat the outlook as directional only."
            },
            Volatility = volatility,
            VolatilityLabel = volatility switch
            {
                MatchupVolatility.Stable => "Relatively stable",
                MatchupVolatility.Volatile => "Volatile",
                MatchupVolatility.Mixed => "Mixed",
                _ => "Unknown"
            },
            AdvantageLabel = diff switch
            {
                null => "Projection advantage unavailable",
                > 0.5m => "You hold the projection advantage",
                < -0.5m => "Opponent holds the projection advantage",
                _ => "Projection is essentially even"
            },
            KeySwings = swings,
            BiggestRisks = risks,
            BiggestAdvantages = advantages,
            LineupImpact = lineupImpact,
            OpponentScout = scout,
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StatusMessage =
                $"Week {league.CurrentWeek} game plan for {TeamLabel(mine)} vs {TeamLabel(opponent)} · {FormatScoring(league.ScoringType)}.",
            GeneratedAt = now
        };
    }

    private SideBoard BuildSide(FantasyTeam team, bool preferStarters)
    {
        var unavailable = new List<string>();
        var starterIds = team.StarterIds.Count > 0
            ? team.StarterIds.ToHashSet()
            : [];

        var rosterPlayers = new List<SidePlayer>();
        foreach (var id in team.PlayerIds)
        {
            var player = _players.GetPlayer(id);
            if (player is null)
            {
                unavailable.Add($"Player {id:N} unavailable in catalog");
                continue;
            }

            var projection = _projections.GetProjection(id);
            var assessment = _assessments.GetAssessment(id);
            var isStarter = starterIds.Count == 0
                ? false
                : starterIds.Contains(id);
            rosterPlayers.Add(new SidePlayer(player, projection, assessment, isStarter));
        }

        IReadOnlyList<SidePlayer> lineup;
        if (preferStarters && starterIds.Count > 0)
        {
            lineup = rosterPlayers.Where(p => p.IsStarter).ToList();
            if (lineup.Count == 0)
            {
                lineup = PickProjectedLineup(rosterPlayers);
            }
        }
        else
        {
            lineup = PickProjectedLineup(rosterPlayers);
        }

        return new SideBoard(team, rosterPlayers, lineup, unavailable);
    }

    private static IReadOnlyList<SidePlayer> PickProjectedLineup(IReadOnlyList<SidePlayer> roster)
    {
        // When starter ids are empty, approximate a lineup from top projected skill players.
        var picks = new List<SidePlayer>();
        foreach (var position in new[] { Position.QB, Position.RB, Position.RB, Position.WR, Position.WR, Position.TE })
        {
            var next = roster
                .Where(p => p.Player.Position == position && picks.All(x => x.Player.Id != p.Player.Id))
                .OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? -1)
                .FirstOrDefault();
            if (next is not null)
            {
                picks.Add(next);
            }
        }

        return picks.Count > 0
            ? picks
            : roster.OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? -1).Take(6).ToList();
    }

    private static decimal? SumProjected(IReadOnlyList<SidePlayer> lineup)
    {
        var values = lineup.Select(p => p.Projection?.ProjectedFantasyPoints).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Sum();
    }

    private static int DeriveMatchupConfidence(SideBoard mine, SideBoard opp, FantasyTeamIntelligenceReport myReport)
    {
        var myCoverage = CoverageRatio(mine.Lineup);
        var oppCoverage = CoverageRatio(opp.Lineup);
        var coverageScore = (int)Math.Round(((myCoverage + oppCoverage) / 2.0) * 55);
        var rosterIntel = myReport.RosterIntelligence.Count == 0
            ? 25
            : (int)Math.Round(myReport.RosterIntelligence.Average(r => r.IntelligenceConfidence));
        var intel = Math.Clamp(rosterIntel / 2, 10, 40);
        var penalty = mine.Unavailable.Count + opp.Unavailable.Count;
        return Math.Clamp(coverageScore + intel - Math.Min(20, penalty * 4), 10, 92);
    }

    private static double CoverageRatio(IReadOnlyList<SidePlayer> lineup)
    {
        if (lineup.Count == 0)
        {
            return 0;
        }

        return lineup.Count(p => p.Projection is not null) / (double)lineup.Count;
    }

    private static MatchupAssessment DeriveAssessment(decimal? diff, int confidence)
    {
        if (diff is null || confidence < 30)
        {
            return MatchupAssessment.Unknown;
        }

        if (diff >= 8)
        {
            return MatchupAssessment.Favorable;
        }

        if (diff <= -8)
        {
            return MatchupAssessment.Challenging;
        }

        return MatchupAssessment.Competitive;
    }

    private static MatchupVolatility DeriveVolatility(SideBoard mine, SideBoard opp)
    {
        var vols = mine.Lineup.Concat(opp.Lineup)
            .Select(p => p.Projection?.Volatility)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();

        if (vols.Count == 0)
        {
            return MatchupVolatility.Unknown;
        }

        var avg = vols.Average();
        if (avg >= 65)
        {
            return MatchupVolatility.Volatile;
        }

        if (avg <= 40)
        {
            return MatchupVolatility.Stable;
        }

        return MatchupVolatility.Mixed;
    }

    private static string BuildAssessmentSummary(
        MatchupAssessment assessment,
        decimal? diff,
        int confidence,
        MatchupVolatility volatility)
    {
        var edge = diff is null ? "Projection edge unavailable." : $"Projected margin {diff:+0.0;-0.0;0.0} pts.";
        var vol = volatility switch
        {
            MatchupVolatility.Volatile => "Lineups look volatile.",
            MatchupVolatility.Stable => "Projection profile looks relatively stable.",
            MatchupVolatility.Mixed => "Volatility is mixed across the two lineups.",
            _ => "Volatility unclear."
        };

        return assessment switch
        {
            MatchupAssessment.Favorable => $"You are favored on projections. {edge} {vol} Confidence {confidence}%.",
            MatchupAssessment.Challenging => $"Opponent is favored on projections. {edge} {vol} Confidence {confidence}%.",
            MatchupAssessment.Competitive => $"This week looks competitive. {edge} {vol} Confidence {confidence}%.",
            _ => $"Matchup assessment is limited by incomplete data. {edge} Confidence {confidence}%."
        };
    }

    private static IReadOnlyList<MatchupSwing> BuildSwings(SideBoard mine, SideBoard opp)
    {
        var swings = new List<MatchupSwing>();

        foreach (var position in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            var myBest = BestAt(mine.Lineup, position);
            var oppBest = BestAt(opp.Lineup, position);
            if (myBest is null && oppBest is null)
            {
                continue;
            }

            var myPts = myBest?.Projection?.ProjectedFantasyPoints;
            var oppPts = oppBest?.Projection?.ProjectedFantasyPoints;
            if (myPts is null && oppPts is null)
            {
                continue;
            }

            if (myPts is not null && (oppPts is null || myPts - oppPts >= 3))
            {
                swings.Add(new MatchupSwing
                {
                    Title = $"{position} edge · {myBest!.Player.FullName}",
                    Detail = oppPts is null
                        ? $"Projects {myPts:0.0} with no clear opponent {position} projection."
                        : $"Projects {myPts:0.0} vs opponent {oppBest!.Player.FullName} at {oppPts:0.0}.",
                    SideLabel = "My team",
                    PlayerId = myBest.Player.Id,
                    Category = "Positional edge"
                });
            }
            else if (oppPts is not null && (myPts is null || oppPts - myPts >= 3))
            {
                swings.Add(new MatchupSwing
                {
                    Title = $"Opponent threat · {oppBest!.Player.FullName}",
                    Detail = myPts is null
                        ? $"Opponent {position} projects {oppPts:0.0}."
                        : $"Projects {oppPts:0.0} vs your {myBest!.Player.FullName} at {myPts:0.0}.",
                    SideLabel = "Opponent",
                    PlayerId = oppBest.Player.Id,
                    Category = "Opponent threat"
                });
            }
        }

        foreach (var player in mine.Lineup
                     .Where(p => p.Projection?.Volatility >= 70)
                     .OrderByDescending(p => p.Projection!.Volatility)
                     .Take(2))
        {
            swings.Add(new MatchupSwing
            {
                Title = $"High volatility · {player.Player.FullName}",
                Detail = $"Volatility {player.Projection!.Volatility} — outcome range can swing this matchup.",
                SideLabel = "My team",
                PlayerId = player.Player.Id,
                Category = "Volatility"
            });
        }

        foreach (var player in mine.Lineup.Where(p => p.Assessment.InjuryProfile?.CurrentInjury is not null).Take(2))
        {
            var injury = player.Assessment.InjuryProfile!.CurrentInjury!;
            swings.Add(new MatchupSwing
            {
                Title = $"Injury swing · {player.Player.FullName}",
                Detail = string.IsNullOrWhiteSpace(injury.BodyPart)
                    ? $"{injury.Status} could materially change your lineup."
                    : $"{injury.Status} ({injury.BodyPart}) could materially change your lineup.",
                SideLabel = "My team",
                PlayerId = player.Player.Id,
                Category = "Injury"
            });
        }

        return swings
            .GroupBy(s => (s.PlayerId, s.Category))
            .Select(g => g.First())
            .Take(6)
            .ToList();
    }

    private static SidePlayer? BestAt(IReadOnlyList<SidePlayer> lineup, Position position) =>
        lineup.Where(p => p.Player.Position == position)
            .OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? -1)
            .FirstOrDefault();

    private static IReadOnlyList<MatchupFactor> BuildRisks(SideBoard mine, FantasyTeamIntelligenceReport report)
    {
        var risks = new List<MatchupFactor>();

        foreach (var alert in report.Alerts.Where(a => a.Severity is TeamAlertSeverity.Urgent or TeamAlertSeverity.Watch).Take(3))
        {
            risks.Add(new MatchupFactor
            {
                Title = alert.Title,
                Detail = alert.Detail,
                PlayerId = alert.PlayerId
            });
        }

        foreach (var player in mine.Lineup
                     .Where(p => p.Assessment.AssessmentConfidence < 40 || p.Projection is null)
                     .OrderBy(p => p.Assessment.AssessmentConfidence)
                     .Take(2))
        {
            risks.Add(new MatchupFactor
            {
                Title = $"Low-confidence projection · {player.Player.FullName}",
                Detail = player.Projection is null
                    ? "Projection unavailable for a likely starter."
                    : $"Intelligence confidence {player.Assessment.AssessmentConfidence}% on a projected starter.",
                PlayerId = player.Player.Id
            });
        }

        foreach (var player in mine.Lineup.Where(p => (p.Projection?.Volatility ?? 0) >= 70).Take(1))
        {
            risks.Add(new MatchupFactor
            {
                Title = $"Volatile starter · {player.Player.FullName}",
                Detail = $"Volatility {player.Projection!.Volatility} raises weekly variance.",
                PlayerId = player.Player.Id
            });
        }

        return risks
            .GroupBy(r => r.Title)
            .Select(g => g.First())
            .Take(5)
            .ToList();
    }

    private static IReadOnlyList<MatchupFactor> BuildAdvantages(
        SideBoard mine,
        SideBoard opp,
        FantasyTeamIntelligenceReport report,
        decimal? diff)
    {
        var advantages = new List<MatchupFactor>();

        if (diff is >= 3)
        {
            advantages.Add(new MatchupFactor
            {
                Title = "Projection edge",
                Detail = $"Your projected lineup leads by {diff:0.0} points under current scoring.",
                PlayerId = null
            });
        }

        foreach (var strength in report.Strengths.Take(2))
        {
            advantages.Add(new MatchupFactor
            {
                Title = "Roster strength",
                Detail = strength,
                PlayerId = null
            });
        }

        foreach (var player in mine.Lineup
                     .Where(p => (p.Assessment.Outlook is PlayerOutlook.Strong or PlayerOutlook.Positive) &&
                                 p.Assessment.AssessmentConfidence >= 55)
                     .OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? 0)
                     .Take(2))
        {
            advantages.Add(new MatchupFactor
            {
                Title = $"High-confidence starter · {player.Player.FullName}",
                Detail =
                    $"{player.Assessment.OutlookLabel} outlook" +
                    (player.Assessment.OpportunityScore is int o ? $" · opportunity {o}" : "") +
                    (player.Projection is { } proj ? $" · {proj.ProjectedFantasyPoints:0.0} pts" : ""),
                PlayerId = player.Player.Id
            });
        }

        foreach (var weakness in BuildOpponentSoftSpots(opp).Take(2))
        {
            advantages.Add(weakness);
        }

        return advantages
            .GroupBy(a => a.Title)
            .Select(g => g.First())
            .Take(5)
            .ToList();
    }

    private static IEnumerable<MatchupFactor> BuildOpponentSoftSpots(SideBoard opp)
    {
        foreach (var position in new[] { Position.RB, Position.WR, Position.TE })
        {
            var group = opp.Lineup.Where(p => p.Player.Position == position).ToList();
            if (group.Count == 0)
            {
                yield return new MatchupFactor
                {
                    Title = $"Opponent {position} gap",
                    Detail = $"No clear opponent {position} in the projected lineup.",
                    PlayerId = null
                };
                continue;
            }

            var avgOpp = group.Average(p => p.Assessment.OpportunityScore ?? 45);
            if (avgOpp <= 40)
            {
                yield return new MatchupFactor
                {
                    Title = $"Soft opponent {position} usage",
                    Detail = $"Opponent {position} opportunity averages {avgOpp:0}.",
                    PlayerId = group.OrderBy(p => p.Assessment.OpportunityScore ?? 45).First().Player.Id
                };
            }
        }
    }

    private static IReadOnlyList<MatchupLineupImpact> BuildLineupImpact(
        IReadOnlyList<StartSitRecommendation> startSit,
        decimal? myScore,
        decimal? oppScore)
    {
        return startSit
            .Take(6)
            .Select(rec =>
            {
                var pts = ParseLeadingPoints(rec.ProjectionSummary);
                var ifStarted = rec.Action == StartSitAction.Start
                    ? pts is null
                        ? "Keeps this player in your projected lineup."
                        : $"Locks in about {pts:0.0} projected points toward your weekly total."
                    : pts is null
                        ? "Would move a lower-graded option into the projected lineup."
                        : $"Would add about {pts:0.0} projected points if elevated over the current lean.";

                var ifSat = rec.Action == StartSitAction.Sit
                    ? pts is null
                        ? "Avoids dedicating a lineup slot to the weaker option."
                        : $"Avoids dedicating a slot to a {pts:0.0}-pt lean."
                    : pts is null
                        ? "Sitting them opens a hole in your projected lineup."
                        : $"Sitting them removes about {pts:0.0} projected points from your side.";

                var relevance = myScore is not null && oppScore is not null && pts is not null
                    ? $"Current projected margin is {myScore - oppScore:+0.0;-0.0;0.0}. This {rec.PositionLabel} decision can move that margin."
                    : $"This {rec.PositionLabel} decision affects your weekly projection vs the opponent.";

                return new MatchupLineupImpact
                {
                    Action = rec.Action,
                    PlayerId = rec.PlayerId,
                    PlayerName = rec.PlayerName,
                    PositionLabel = rec.PositionLabel,
                    ProjectionSummary = rec.ProjectionSummary,
                    Confidence = rec.Confidence,
                    InsufficientData = rec.InsufficientData,
                    MatchupRelevance = relevance,
                    IfStarted = ifStarted,
                    IfSat = ifSat,
                    Reasons = rec.Reasons
                };
            })
            .ToList();
    }

    private static decimal? ParseLeadingPoints(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var token = summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return decimal.TryParse(token, out var value) ? value : null;
    }

    private OpponentScoutReport BuildOpponentScout(FantasyTeam opponent, SideBoard side)
    {
        var strengths = new List<string>();
        var weaknesses = new List<string>();
        foreach (var position in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            var group = side.Lineup.Where(p => p.Player.Position == position).ToList();
            if (group.Count == 0)
            {
                weaknesses.Add($"Thin {position} presence in projected lineup.");
                continue;
            }

            var top = group.OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? -1).First();
            if (top.Projection?.ProjectedFantasyPoints >= 12)
            {
                strengths.Add($"{position} threat: {top.Player.FullName} ({top.Projection.ProjectedFantasyPoints:0.0} pts).");
            }

            if (group.Average(p => p.Assessment.OpportunityScore ?? 45) <= 40)
            {
                weaknesses.Add($"{position} opportunity looks soft.");
            }
        }

        var threats = side.Lineup
            .OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? -1)
            .Take(3)
            .Select(p => new MatchupFactor
            {
                Title = p.Player.FullName,
                Detail = p.Projection is null
                    ? $"{p.Player.Position} · projection unavailable"
                    : $"{p.Player.Position} · {p.Projection.ProjectedFantasyPoints:0.0} pts · intel {p.Assessment.AssessmentConfidence}%",
                PlayerId = p.Player.Id
            })
            .ToList();

        var news = side.Roster
            .SelectMany(p => p.Assessment.RecentIntelligence.Select(n => (Player: p, News: n)))
            .OrderByDescending(x => x.News.IsConfirmed)
            .ThenByDescending(x => x.News.Timestamp)
            .Take(3)
            .Select(x => new MatchupFactor
            {
                Title = $"{x.News.VerificationLabel} · {x.Player.Player.FullName}",
                Detail = x.News.Title,
                PlayerId = x.Player.Player.Id
            })
            .ToList();

        var swingPlayers = side.Lineup
            .OrderByDescending(p => p.Projection?.ProjectedFantasyPoints ?? -1)
            .Take(4)
            .Select(p => new MatchupSwingPlayer
            {
                PlayerId = p.Player.Id,
                PlayerName = p.Player.FullName,
                PositionLabel = p.Player.Position.ToString(),
                ProjectionSummary = p.Projection is null ? null : $"{p.Projection.ProjectedFantasyPoints:0.0} pts",
                Note = p.Assessment.Outlook == PlayerOutlook.Concerning
                    ? "Concerning outlook — monitor closely."
                    : p.Projection?.Volatility >= 70
                        ? "High-volatility swing candidate."
                        : "Primary projection contributor."
            })
            .ToList();

        return new OpponentScoutReport
        {
            TeamName = TeamLabel(opponent),
            Strengths = strengths.Take(3).ToList(),
            Weaknesses = weaknesses.Take(3).ToList(),
            BiggestThreats = threats,
            RelevantNews = news,
            SwingPlayers = swingPlayers,
            StatusMessage = strengths.Count == 0 && threats.Count == 0
                ? "Opponent scout is limited by incomplete projection coverage."
                : $"Scout based on {side.Lineup.Count} projected lineup players."
        };
    }

    private WeeklyMatchupGamePlan Empty(
        PersonalizedAnalysisContext context,
        string message,
        DateTimeOffset now,
        League? league = null,
        FantasyTeam? mine = null) =>
        new()
        {
            LeagueId = context.LeagueId,
            SelectedRosterId = context.SelectedRosterId,
            OpponentRosterId = null,
            LeagueName = context.LeagueName,
            Week = context.Week,
            ScoringLabel = FormatScoring(context.ScoringType),
            IsSetupComplete = context.IsSetupComplete,
            HasMatchup = false,
            MyTeamName = context.TeamName ?? mine?.DisplayName ?? "No team selected",
            OpponentTeamName = "No opponent",
            OpponentSource = MatchupOpponentSource.None,
            MyProjectedScore = null,
            OpponentProjectedScore = null,
            ProjectionDifference = null,
            Assessment = MatchupAssessment.Unknown,
            AssessmentLabel = "Unknown",
            AssessmentSummary = message,
            MatchupConfidence = 10,
            ConfidenceNote = "Insufficient matchup context.",
            Volatility = MatchupVolatility.Unknown,
            VolatilityLabel = "Unknown",
            AdvantageLabel = "Unavailable",
            KeySwings = [],
            BiggestRisks = [],
            BiggestAdvantages = [],
            LineupImpact = [],
            OpponentScout = new OpponentScoutReport
            {
                TeamName = "No opponent",
                Strengths = [],
                Weaknesses = [],
                BiggestThreats = [],
                RelevantNews = [],
                SwingPlayers = [],
                StatusMessage = message
            },
            UnavailableSignals = [],
            StatusMessage = message,
            GeneratedAt = now
        };

    private static string TeamLabel(FantasyTeam team) =>
        string.IsNullOrWhiteSpace(team.TeamName) ? team.DisplayName : team.TeamName!;

    private static string FormatScoring(ScoringType scoring) => scoring switch
    {
        ScoringType.Ppr => "PPR",
        ScoringType.HalfPpr => "Half PPR",
        ScoringType.Standard => "Standard",
        _ => scoring.ToString()
    };

    private sealed record SidePlayer(
        Player Player,
        PlayerProjection? Projection,
        PlayerIntelligenceAssessment Assessment,
        bool IsStarter);

    private sealed record SideBoard(
        FantasyTeam Team,
        IReadOnlyList<SidePlayer> Roster,
        IReadOnlyList<SidePlayer> Lineup,
        IReadOnlyList<string> Unavailable);
}
