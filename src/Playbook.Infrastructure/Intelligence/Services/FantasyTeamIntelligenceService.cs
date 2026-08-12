using Playbook.Application.Abstractions;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Knowledge;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Knowledge;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Fantasy team intelligence composed from existing assessment + projection services.
/// Start/Sit recommendations are produced by the centralized decision engine,
/// consuming PlayerKnowledge via the shared knowledge / PredictionContext layer.
/// Invalidates whenever league / owned-team context changes.
/// </summary>
public sealed class FantasyTeamIntelligenceService : IFantasyTeamIntelligenceService
{
    private readonly ILeagueState _leagueState;
    private readonly IPlayerService _players;
    private readonly IPlayerIntelligenceAssessmentService _assessments;
    private readonly IProjectionService _projections;
    private readonly IPlayerKnowledgeComposer _knowledgeComposer;
    private readonly ISharedKnowledgeModel _sharedKnowledge;
    private readonly IKnowledgeImpactApplicator _knowledgeImpact;
    private readonly IDecisionEngine _decisionEngine;
    private readonly object _gate = new();

    private FantasyTeamIntelligenceReport? _cached;
    private PersonalizedAnalysisContext _cachedContext;

    public FantasyTeamIntelligenceService(
        ILeagueState leagueState,
        IPlayerService players,
        IPlayerIntelligenceAssessmentService assessments,
        IProjectionService projections,
        IPlayerKnowledgeComposer knowledgeComposer,
        ISharedKnowledgeModel sharedKnowledge,
        IKnowledgeImpactApplicator knowledgeImpact,
        IDecisionEngine decisionEngine)
    {
        _leagueState = leagueState;
        _players = players;
        _assessments = assessments;
        _projections = projections;
        _knowledgeComposer = knowledgeComposer;
        _sharedKnowledge = sharedKnowledge;
        _knowledgeImpact = knowledgeImpact;
        _decisionEngine = decisionEngine;
        _leagueState.Changed += OnLeagueContextChanged;
    }

    public FantasyTeamIntelligenceReport GetReport()
    {
        var context = PersonalizedAnalysisContext.FromState(_leagueState);
        if (_cached is not null &&
            context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
            context.ScoringType == _cachedContext.ScoringType &&
            context.Week == _cachedContext.Week)
        {
            return _cached;
        }

        lock (_gate)
        {
            context = PersonalizedAnalysisContext.FromState(_leagueState);
            if (_cached is not null &&
                context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
                context.ScoringType == _cachedContext.ScoringType &&
                context.Week == _cachedContext.Week)
            {
                return _cached;
            }

            _cached = BuildReport(context);
            _cachedContext = context;
            return _cached;
        }
    }

    private void OnLeagueContextChanged()
    {
        lock (_gate)
        {
            _cached = null;
            _cachedContext = default;
        }
    }

    private FantasyTeamIntelligenceReport BuildReport(PersonalizedAnalysisContext context)
    {
        var league = _leagueState.CurrentLeague;
        var team = _leagueState.CurrentUserTeam;
        var now = DateTimeOffset.UtcNow;

        if (league is null)
        {
            return Empty(
                context,
                "Select or connect a league to see fantasy team intelligence.",
                now);
        }

        if (!context.IsSetupComplete || team is null)
        {
            return Empty(
                context,
                "Pick your owned team in the league switcher to generate roster intelligence.",
                now,
                league);
        }

        var rosterIds = team.PlayerIds;
        if (rosterIds.Count == 0)
        {
            return Empty(
                context,
                "This team has no roster players loaded yet. Connect a live Sleeper league or wait for roster sync.",
                now,
                league,
                team);
        }

        var starterSet = team.StarterIds.ToHashSet();
        var rows = new List<RosterRow>();
        var unavailable = new List<string>();

        foreach (var playerId in rosterIds)
        {
            var player = _players.GetPlayer(playerId);
            if (player is null)
            {
                unavailable.Add($"Player {playerId:N} unavailable in catalog");
                continue;
            }

            var assessment = _assessments.GetAssessment(playerId);
            var projection = assessment.Projection ?? _projections.GetProjection(playerId);
            rows.Add(new RosterRow(player, starterSet.Contains(playerId), assessment, projection));
        }

        if (rows.Count == 0)
        {
            return Empty(
                context,
                "Roster players could not be resolved from the player catalog.",
                now,
                league,
                team,
                unavailable);
        }

        var decisionContext = DecisionContext.FromLeague(league, team, DecisionKind.StartSit);
        var startSit = BuildStartSitViaEngine(rows, decisionContext);
        var alerts = BuildAlerts(rows, startSit);
        var rosterLimit = RosterLimitReconciler.Check(team, league);
        if (rosterLimit is { IsKnown: true, IsOverLimit: true })
        {
            alerts =
            [
                new TeamRosterAlert
                {
                    Title = "Roster over the configured limit",
                    Detail = rosterLimit.Message,
                    Severity = TeamAlertSeverity.Watch,
                    PlayerId = null,
                    Category = "Roster"
                },
                .. alerts
            ];
        }

        var rosterIntel = BuildRosterIntelligence(rows, alerts);
        var (strengths, weaknesses, concerns) = BuildStrengthWeakness(rows);
        var whatMatters = BuildWhatMatters(rows, alerts, strengths, weaknesses, concerns);
        var (outlookLabel, outlookDetail) = DeriveRosterOutlook(rows, concerns);

        return new FantasyTeamIntelligenceReport
        {
            LeagueId = league.Id,
            SelectedRosterId = team.RosterId,
            LeagueName = league.Name,
            TeamName = context.TeamName ?? team.DisplayName,
            LeagueTypeLabel = FormatLeagueType(league.LeagueType),
            ScoringLabel = FormatScoring(league.ScoringType),
            Week = league.CurrentWeek,
            IsSetupComplete = true,
            HasRosterPlayers = true,
            RosterOutlookLabel = outlookLabel,
            RosterOutlookDetail = outlookDetail,
            Strengths = strengths,
            Weaknesses = weaknesses,
            ImmediateConcerns = concerns,
            WhatMatters = whatMatters,
            StartSit = startSit,
            RosterIntelligence = rosterIntel,
            Alerts = alerts,
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StatusMessage = $"{rows.Count} roster players analyzed for {context.DisplayLabel}.",
            GeneratedAt = now
        };
    }

    private static FantasyTeamIntelligenceReport Empty(
        PersonalizedAnalysisContext context,
        string message,
        DateTimeOffset now,
        League? league = null,
        FantasyTeam? team = null,
        IReadOnlyList<string>? unavailable = null) =>
        new()
        {
            LeagueId = context.LeagueId,
            SelectedRosterId = context.SelectedRosterId,
            LeagueName = context.LeagueName,
            TeamName = context.TeamName ?? team?.DisplayName ?? "No team selected",
            LeagueTypeLabel = league is null ? "—" : FormatLeagueType(league.LeagueType),
            ScoringLabel = FormatScoring(context.ScoringType),
            Week = context.Week,
            IsSetupComplete = context.IsSetupComplete,
            HasRosterPlayers = false,
            RosterOutlookLabel = null,
            RosterOutlookDetail = null,
            Strengths = [],
            Weaknesses = [],
            ImmediateConcerns = [],
            WhatMatters = [],
            StartSit = [],
            RosterIntelligence = [],
            Alerts = [],
            UnavailableSignals = unavailable?.ToList() ?? [],
            StatusMessage = message,
            GeneratedAt = now
        };

    private IReadOnlyList<StartSitRecommendation> BuildStartSitViaEngine(
        IReadOnlyList<RosterRow> rows,
        DecisionContext decisionContext)
    {
        // Compose raw player knowledge, then route through shared PredictionContext.
        var composed = rows
            .Select(r => _knowledgeComposer.ComposeAsync(r.Player, decisionContext).GetAwaiter().GetResult())
            .ToList();

        var predictionContexts = composed
            .Select((k, i) => _sharedKnowledge.BuildStartSitPredictionContext(
                k,
                decisionContext,
                team: rows[i].Player.Team,
                opponentTeam: null))
            .ToList();

        foreach (var ctx in predictionContexts)
        {
            if (ctx.PredictionType != PredictionType.StartSit)
            {
                throw new InvalidOperationException("Start/Sit path received non-StartSit PredictionContext.");
            }

            KnowledgeTemporalGuard.AssertNoFutureLeak(ctx.Knowledge, decisionContext.InformationCutoff);
        }

        var assembled = predictionContexts
            .Select(c => c.PlayerKnowledge
                ?? throw new InvalidOperationException(
                    $"Shared knowledge missing DecisionPlayerKnowledge for {c.PlayerName}."))
            .ToList();
        var knowledge = _knowledgeImpact.ApplyToPlayerKnowledgeBatch(assembled);

        var candidates = rows
            .Select(r => new StartSitCandidate
            {
                PlayerId = r.Player.Id,
                PlayerName = r.Player.FullName,
                Position = r.Player.Position,
                IsStarter = r.IsStarter
            })
            .ToList();

        var batch = _decisionEngine
            .EvaluateStartSitAsync(knowledge, candidates, decisionContext)
            .GetAwaiter()
            .GetResult();

        return batch.Recommendations;
    }

    private static IReadOnlyList<TeamRosterAlert> BuildAlerts(
        IReadOnlyList<RosterRow> rows,
        IReadOnlyList<StartSitRecommendation> startSit)
    {
        var alerts = new List<TeamRosterAlert>();

        foreach (var row in rows.OrderByDescending(r => r.IsStarter))
        {
            var a = row.Assessment;
            if (a.InjuryProfile?.CurrentInjury is { } current)
            {
                alerts.Add(new TeamRosterAlert
                {
                    Title = $"Injury concern · {row.Player.FullName}",
                    Detail = string.IsNullOrWhiteSpace(current.BodyPart)
                        ? $"{current.Status}"
                        : $"{current.Status} — {current.BodyPart}",
                    Severity = row.IsStarter ? TeamAlertSeverity.Urgent : TeamAlertSeverity.Watch,
                    PlayerId = row.Player.Id,
                    Category = "Injury"
                });
            }
            else if (a.InjuryProfile is { UnconfirmedSignals.Count: > 0 })
            {
                alerts.Add(new TeamRosterAlert
                {
                    Title = $"Unconfirmed injury report · {row.Player.FullName}",
                    Detail = a.InjuryProfile.UnconfirmedSignals[0].Headline,
                    Severity = TeamAlertSeverity.Watch,
                    PlayerId = row.Player.Id,
                    Category = "Injury"
                });
            }

            if (a.Outlook == PlayerOutlook.Concerning && a.InjuryProfile?.CurrentInjury is null)
            {
                alerts.Add(new TeamRosterAlert
                {
                    Title = $"Concerning outlook · {row.Player.FullName}",
                    Detail = a.Headline,
                    Severity = row.IsStarter ? TeamAlertSeverity.Watch : TeamAlertSeverity.Info,
                    PlayerId = row.Player.Id,
                    Category = "Outlook"
                });
            }

            if (a.OpportunityScore is int opp and <= 35 && row.IsStarter)
            {
                alerts.Add(new TeamRosterAlert
                {
                    Title = $"Usage/opportunity drop · {row.Player.FullName}",
                    Detail = $"Opportunity score {opp}/100 while listed as a starter.",
                    Severity = TeamAlertSeverity.Watch,
                    PlayerId = row.Player.Id,
                    Category = "Usage"
                });
            }

            var news = a.RecentIntelligence.FirstOrDefault(i => i.IsConfirmed)
                       ?? a.RecentIntelligence.FirstOrDefault();
            if (news is not null &&
                (news.Category.Contains("Injury", StringComparison.OrdinalIgnoreCase) ||
                 news.Category.Contains("Situation", StringComparison.OrdinalIgnoreCase) ||
                 news.IsConfirmed) &&
                (DateTimeOffset.UtcNow - news.Timestamp).TotalDays <= 10)
            {
                alerts.Add(new TeamRosterAlert
                {
                    Title = $"{(news.IsConfirmed ? "Confirmed" : news.VerificationLabel)} news · {row.Player.FullName}",
                    Detail = news.Title,
                    Severity = news.IsConfirmed ? TeamAlertSeverity.Watch : TeamAlertSeverity.Info,
                    PlayerId = row.Player.Id,
                    Category = "News"
                });
            }
        }

        foreach (var sit in startSit.Where(s => s.Action == StartSitAction.Sit && !s.InsufficientData))
        {
            var row = rows.First(r => r.Player.Id == sit.PlayerId);
            if (!row.IsStarter)
            {
                continue;
            }

            if (alerts.Any(a => a.PlayerId == sit.PlayerId && a.Category == "Lineup"))
            {
                continue;
            }

            alerts.Add(new TeamRosterAlert
            {
                Title = $"Potential lineup issue · {sit.PlayerName}",
                Detail = sit.Reasons.FirstOrDefault() ?? "A bench option currently grades higher.",
                Severity = TeamAlertSeverity.Watch,
                PlayerId = sit.PlayerId,
                Category = "Lineup"
            });
        }

        return alerts
            .GroupBy(a => (a.PlayerId, a.Category, a.Title))
            .Select(g => g.First())
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Title)
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<RosterPlayerIntelligence> BuildRosterIntelligence(
        IReadOnlyList<RosterRow> rows,
        IReadOnlyList<TeamRosterAlert> alerts)
    {
        var alerted = alerts.Select(a => a.PlayerId).Where(id => id is not null).Select(id => id!.Value).ToHashSet();

        return rows
            .Select(row =>
            {
                var a = row.Assessment;
                var news = a.RecentIntelligence.FirstOrDefault();
                var signals = new List<string>();
                if (a.ProjectionSummary is not null)
                {
                    signals.Add($"Projection {a.ProjectionSummary}");
                }

                if (a.OpportunityScore is int o)
                {
                    signals.Add($"Opportunity {o}");
                }

                if (a.UsageScore is int u)
                {
                    signals.Add($"Usage {u}");
                }

                signals.Add($"Health: {a.HealthStatusLabel}");
                if (a.KeyFactors.Count > 0)
                {
                    signals.Add(a.KeyFactors[0].Text);
                }

                var priority =
                    (row.IsStarter ? 20 : 0) +
                    (alerted.Contains(row.Player.Id) ? 30 : 0) +
                    (a.Outlook == PlayerOutlook.Concerning ? 15 : 0) +
                    (int)(row.Projection?.ProjectedFantasyPoints ?? 0);

                return new RosterPlayerIntelligence
                {
                    PlayerId = row.Player.Id,
                    PlayerName = row.Player.FullName,
                    PositionLabel = row.Player.Position.ToString(),
                    IsStarter = row.IsStarter,
                    OutlookLabel = a.OutlookLabel,
                    ProjectionSummary = FormatProjection(row.Projection),
                    OpportunityScore = a.OpportunityScore,
                    UsageScore = a.UsageScore,
                    HealthLabel = a.HealthStatusLabel,
                    IntelligenceConfidence = a.AssessmentConfidence,
                    TrendLabel = a.Profile?.TrendDirection.ToString(),
                    TopNews = news?.Title,
                    NewsConfirmed = news?.IsConfirmed ?? false,
                    KeySignals = signals.Take(4).ToList(),
                    Priority = priority
                };
            })
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.PlayerName)
            .ToList();
    }

    private static (IReadOnlyList<string> Strengths, IReadOnlyList<string> Weaknesses, IReadOnlyList<string> Concerns)
        BuildStrengthWeakness(IReadOnlyList<RosterRow> rows)
    {
        var strengths = new List<string>();
        var weaknesses = new List<string>();
        var concerns = new List<string>();

        foreach (var pos in new[] { Position.RB, Position.WR, Position.QB, Position.TE })
        {
            var group = rows.Where(r => r.Player.Position == pos).ToList();
            if (group.Count == 0)
            {
                weaknesses.Add($"No {pos} on roster.");
                continue;
            }

            var avgOpp = group.Average(r => r.Assessment.OpportunityScore ?? 45);
            var healthy = group.Count(r =>
                r.Assessment.InjuryProfile?.CurrentInjury is null &&
                !r.Assessment.HealthStatusLabel.Contains("concern", StringComparison.OrdinalIgnoreCase));

            if (group.Count >= 3 && avgOpp >= 55)
            {
                strengths.Add($"{pos} depth looks solid ({group.Count} players, avg opportunity {avgOpp:0}).");
            }
            else if (group.Count <= 1)
            {
                weaknesses.Add($"{pos} depth is thin ({group.Count} player).");
            }
            else if (avgOpp < 45)
            {
                weaknesses.Add($"{pos} opportunity is soft across the group.");
            }

            if (healthy < group.Count)
            {
                concerns.Add($"{group.Count - healthy} {pos} health item(s) need monitoring.");
            }
        }

        var top = rows.OrderByDescending(r => r.Projection?.ProjectedFantasyPoints ?? 0).FirstOrDefault();
        if (top is not null && top.Assessment.AssessmentConfidence < 45)
        {
            concerns.Add($"{top.Player.FullName} carries a strong projection role but limited intelligence confidence ({top.Assessment.AssessmentConfidence}%).");
        }

        var injuredStarters = rows.Where(r => r.IsStarter && r.Assessment.InjuryProfile?.CurrentInjury is not null).ToList();
        foreach (var row in injuredStarters.Take(2))
        {
            concerns.Add($"{row.Player.FullName}'s health is an immediate starter concern.");
        }

        return (
            strengths.Take(3).ToList(),
            weaknesses.Take(3).ToList(),
            concerns.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList());
    }

    private static IReadOnlyList<string> BuildWhatMatters(
        IReadOnlyList<RosterRow> rows,
        IReadOnlyList<TeamRosterAlert> alerts,
        IReadOnlyList<string> strengths,
        IReadOnlyList<string> weaknesses,
        IReadOnlyList<string> concerns)
    {
        var items = new List<string>();

        foreach (var concern in concerns.Take(2))
        {
            items.Add(concern);
        }

        foreach (var alert in alerts.Where(a => a.Severity == TeamAlertSeverity.Urgent).Take(2))
        {
            if (items.All(i => !i.Contains(alert.Title.Split('·').Last().Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                items.Add($"{alert.Title}: {alert.Detail}");
            }
        }

        foreach (var weakness in weaknesses.Take(1))
        {
            items.Add(weakness);
        }

        foreach (var strength in strengths.Take(1))
        {
            items.Add(strength);
        }

        var lowIntelStarter = rows
            .Where(r => r.IsStarter)
            .OrderBy(r => r.Assessment.AssessmentConfidence)
            .FirstOrDefault();
        if (lowIntelStarter is not null &&
            lowIntelStarter.Assessment.AssessmentConfidence < 50 &&
            items.Count < 4)
        {
            items.Add(
                $"{lowIntelStarter.Player.FullName} has a starter role but intelligence confidence is only {lowIntelStarter.Assessment.AssessmentConfidence}%.");
        }

        return items
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static (string? Label, string? Detail) DeriveRosterOutlook(
        IReadOnlyList<RosterRow> rows,
        IReadOnlyList<string> concerns)
    {
        if (rows.Count == 0)
        {
            return (null, null);
        }

        var concerning = rows.Count(r => r.Assessment.Outlook == PlayerOutlook.Concerning);
        var positive = rows.Count(r => r.Assessment.Outlook is PlayerOutlook.Strong or PlayerOutlook.Positive);
        var avgConfidence = rows.Average(r => r.Assessment.AssessmentConfidence);

        if (avgConfidence < 40)
        {
            return ("Limited", "Roster outlook is constrained by incomplete intelligence coverage.");
        }

        if (concerning >= 2 || concerns.Count >= 3)
        {
            return ("Concerning", "Multiple roster variables need attention before lineup lock.");
        }

        if (positive >= Math.Max(2, rows.Count / 3) && concerning == 0)
        {
            return ("Positive", "More supportive player outlooks than material concerns on the current roster.");
        }

        return ("Stable", "No single directional signal dominates the roster right now.");
    }

    private static string? FormatProjection(PlayerProjection? projection) =>
        projection is null ? null : $"{projection.ProjectedFantasyPoints:0.0} pts";

    private static string FormatScoring(ScoringType scoring) => scoring switch
    {
        ScoringType.Ppr => "PPR",
        ScoringType.HalfPpr => "Half PPR",
        ScoringType.Standard => "Standard",
        _ => scoring.ToString()
    };

    private static string FormatLeagueType(LeagueType type) => type switch
    {
        LeagueType.Redraft => "Redraft",
        LeagueType.Dynasty => "Dynasty",
        LeagueType.Keeper => "Keeper",
        _ => type.ToString()
    };

    private sealed record RosterRow(
        Player Player,
        bool IsStarter,
        PlayerIntelligenceAssessment Assessment,
        PlayerProjection? Projection);
}
