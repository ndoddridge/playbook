using Playbook.Core.Draft;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Draft.Decision;

/// <summary>
/// Staged draft decision layer: hard constraints → preferences → roster fit → future look-ahead.
/// Preferences guide; intelligence decides. Never returns a pick solely for highest generic score.
/// </summary>
public static class DraftDecisionEngine
{
    public const int MaxRecommendations = 3;

    /// <summary>
    /// Preference can break a close call but cannot override a large construction/future gap.
    /// </summary>
    public const decimal PreferenceMaxBoost = 4.0m;

    /// <summary>Meaningful construction gap that can beat a VeryHigh preference.</summary>
    public const decimal PreferenceOverrideGap = 3.5m;

    public sealed class CandidateInput
    {
        public required Player Player { get; init; }
        public required DraftRecommendation BaseRecommendation { get; init; }
        public required decimal TeamFitScore { get; init; }
        public required decimal UpsideCeiling { get; init; }
        public required decimal Floor { get; init; }
        public required AvailabilityRisk AvailabilityRisk { get; init; }
        public decimal? ValueOverReplacement { get; init; }
    }

    public static IReadOnlyList<DraftPickRecommendation> Select(
        IReadOnlyList<CandidateInput> candidates,
        DraftStrategyState state,
        IReadOnlyDictionary<string, int> rosterCounts)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        // Stage 1 — hard constraints (narrow field; soft phase intent is not hard).
        var eligible = candidates
            .Where(c => PassesHardConstraints(c, state))
            .ToList();
        if (eligible.Count == 0)
        {
            eligible = candidates.ToList();
        }

        // Stage 2–4 — score with preferences, construction, urgency, future path.
        var evaluated = eligible
            .Select(c => Evaluate(c, state, rosterCounts))
            .OrderByDescending(e => e.DecisionScore)
            .ToList();

        return AssignRoles(evaluated, state);
    }

    private static bool PassesHardConstraints(CandidateInput c, DraftStrategyState state)
    {
        var pos = c.BaseRecommendation.PositionLabel;

        if (state.SkipKickerAndDst &&
            (pos.Equals("K", StringComparison.OrdinalIgnoreCase)
             || pos.Equals("DST", StringComparison.OrdinalIgnoreCase)
             || pos.Equals("DEF", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static Evaluated Evaluate(
        CandidateInput c,
        DraftStrategyState state,
        IReadOnlyDictionary<string, int> baseCounts)
    {
        var pos = c.BaseRecommendation.PositionLabel;
        var fit = c.TeamFitScore;
        var strategic = 0m;
        var urgency = 0m;
        var upside = c.UpsideCeiling;
        var bullets = new List<string>();

        // Phase intent (soft).
        if (state.TargetPositions.Any(t => t.Equals(pos, StringComparison.OrdinalIgnoreCase)))
        {
            strategic += 2.5m;
            bullets.Add($"Fits current plan ({state.IntentSummary})");
        }
        else if (state.TargetPositions.Count > 0)
        {
            strategic -= 1.0m;
        }

        // Roster construction — over-concentration suppresses PRIMARY RB after deep RB room.
        var current = baseCounts.GetValueOrDefault(pos, 0);
        var construction = ClassifyDepth(pos, current, baseCounts);
        strategic += construction.Score;
        if (!string.IsNullOrEmpty(construction.Note))
        {
            bullets.Add(construction.Note);
        }

        // User preference strength (bounded).
        var pref = state.Plan.PreferredPlayers
            .FirstOrDefault(p => DraftStrategyState.NamesMatch(p.PlayerName, c.Player.FullName));
        if (pref is not null)
        {
            var boost = pref.Strength switch
            {
                PreferenceStrength.VeryHigh => PreferenceMaxBoost,
                PreferenceStrength.High => PreferenceMaxBoost * 0.7m,
                _ => PreferenceMaxBoost * 0.4m
            };
            strategic += boost;
            bullets.Add($"Preferred ({pref.Strength})");
        }

        // Fades — require meaningful value to stay competitive.
        if (state.ActiveFades.Any(f => DraftStrategyState.NamesMatch(f, c.Player.FullName)))
        {
            strategic -= PreferenceMaxBoost;
            bullets.Add("On your fade list — needs clear value to stay here");
        }

        strategic += EvaluateConditionals(c.Player.FullName, state, bullets);

        // Kyler late special rule.
        if (state.PreferKylerLate &&
            DraftStrategyState.NamesMatch(c.Player.FullName, state.Plan.SpecialLateRules.KylerPlayerName))
        {
            strategic += PreferenceMaxBoost;
            bullets.Add("Late QB2 priority with Herbert rostered");
        }

        // Urgency from availability risk.
        urgency += c.AvailabilityRisk switch
        {
            AvailabilityRisk.AtRisk => 3.0m,
            AvailabilityRisk.Safe => -1.5m,
            _ => 0m
        };
        if (c.AvailabilityRisk == AvailabilityRisk.AtRisk)
        {
            bullets.Add("Less likely to survive to your next pick");
        }

        // Look-ahead path quality.
        var lookAhead = BuildLookAhead(c, state, baseCounts);
        var futureScore = ScoreLookAhead(lookAhead, state);
        strategic += futureScore;
        if (futureScore > 0)
        {
            bullets.Add("Creates a strong next 2–3 pick path");
        }

        // VOR scarcity.
        if (c.ValueOverReplacement is > 2m)
        {
            strategic += 1.5m;
            bullets.Add("Meaningful positional scarcity");
        }

        if (bullets.Count == 0)
        {
            bullets.Add("Competitive on production and roster fit");
        }

        // Decision score: fit is base; strategy/urgency/future adjust. Preference cannot erase a
        // large negative construction gap (PreferenceOverrideGap).
        var decision = fit + strategic + urgency * 0.5m;
        if (construction.Score <= -2.5m && pref is not null)
        {
            // Preference still applies, but cannot fully cancel over-concentration.
            decision -= PreferenceOverrideGap * 0.5m;
        }

        return new Evaluated
        {
            Input = c,
            DecisionScore = decision,
            FitScore = fit,
            StrategicScore = strategic,
            UrgencyScore = urgency,
            UpsideScore = upside,
            WhyBullets = bullets.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList(),
            LookAhead = lookAhead
        };
    }

    private static decimal EvaluateConditionals(string playerName, DraftStrategyState state, List<string> bullets)
    {
        var delta = 0m;
        foreach (var rule in state.Plan.ConditionalPreferences)
        {
            if (!DraftStrategyState.NamesMatch(rule.PlayerName, playerName))
            {
                continue;
            }

            if (rule.Effect == ConditionalEffect.DowngradeWhenPresent &&
                state.RosteredPlayerNames.Any(n => DraftStrategyState.NamesMatch(n, rule.ConditionPlayerName)))
            {
                delta -= PreferenceMaxBoost;
                bullets.Add(rule.Explanation);
            }
        }

        return delta;
    }

    private static (decimal Score, string Note) ClassifyDepth(
        string position,
        int count,
        IReadOnlyDictionary<string, int> allCounts)
    {
        // Explicit RB-depth suppression for PRIMARY — addresses Kelce → Rodriguez regression.
        if (position.Equals("RB", StringComparison.OrdinalIgnoreCase))
        {
            if (count >= 3)
            {
                return (-3.5m, $"Already have {count} RBs — additional RB is depth/upside, not a need");
            }

            if (count >= 2)
            {
                return (-1.0m, "RB starters covered — further RBs compete as upside");
            }

            return (1.5m, "RB room still needs bodies");
        }

        if (position.Equals("WR", StringComparison.OrdinalIgnoreCase))
        {
            if (count <= 1)
            {
                return (2.5m, "WR room still needs bodies");
            }

            if (count <= 3)
            {
                // When RB is already deep, WR depth has greater marginal value.
                var rb = allCounts.GetValueOrDefault("RB", 0);
                if (rb >= 3)
                {
                    return (2.0m, "WR depth has greater marginal value than another RB");
                }

                return (1.0m, "WR depth still useful");
            }

            return (-1.5m, "WR depth already solid");
        }

        if (position.Equals("TE", StringComparison.OrdinalIgnoreCase))
        {
            if (count < 1)
            {
                return (2.0m, "Still need a TE");
            }

            return (-0.5m, "TE already rostered — only take clear value");
        }

        if (position.Equals("QB", StringComparison.OrdinalIgnoreCase))
        {
            if (count < 1)
            {
                return (1.5m, "Still need a QB");
            }

            return (-0.5m, "QB already rostered");
        }

        return (0m, "");
    }

    private static IReadOnlyList<DraftLookAheadStep> BuildLookAhead(
        CandidateInput candidate,
        DraftStrategyState state,
        IReadOnlyDictionary<string, int> baseCounts)
    {
        // Deterministic approximation: draft candidate, bump count, project next 2–3 rounds'
        // soft targets from phase intents + resulting needs. No Monte Carlo.
        var counts = new Dictionary<string, int>(baseCounts, StringComparer.OrdinalIgnoreCase);
        var pos = candidate.BaseRecommendation.PositionLabel;
        counts[pos] = counts.GetValueOrDefault(pos, 0) + 1;

        var steps = new List<DraftLookAheadStep>();
        for (var i = 1; i <= 3; i++)
        {
            var round = state.Round + i;
            var intent = state.Plan.PhaseIntents.FirstOrDefault(p => round >= p.FromRound && round <= p.ToRound)
                         ?? state.Plan.PhaseIntents.Last();
            var targetPos = PickNextPosition(counts, intent.PreferredPositions, state);
            var likely = state.Plan.PreferredPlayers
                .Where(p => p.PositionHint is null
                            || p.PositionHint.Equals(targetPos, StringComparison.OrdinalIgnoreCase))
                .Where(p => !DraftStrategyState.NamesMatch(p.PlayerName, candidate.Player.FullName))
                .Where(p => !state.RosteredPlayerNames.Any(n => DraftStrategyState.NamesMatch(n, p.PlayerName)))
                .Take(2)
                .Select(p => p.PlayerName)
                .ToList();

            steps.Add(new DraftLookAheadStep
            {
                Round = round,
                TargetPosition = targetPos,
                LikelyTargets = likely,
                Explanation = $"R{round} → {targetPos}" +
                              (likely.Count > 0 ? $" ({string.Join(", ", likely)})" : " best available fit")
            });

            counts[targetPos] = counts.GetValueOrDefault(targetPos, 0) + 1;
        }

        return steps;
    }

    private static string PickNextPosition(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyList<string> preferred,
        DraftStrategyState state)
    {
        foreach (var p in preferred)
        {
            var n = counts.GetValueOrDefault(p, 0);
            var need = p switch
            {
                "RB" => n < 2,
                "WR" => n < 3,
                "QB" => n < 1 || (state.PreferKylerLate && n < 2),
                "TE" => n < 1,
                _ => n < 1
            };
            if (need)
            {
                return p;
            }
        }

        if (preferred.Count > 0)
        {
            return preferred.OrderBy(p => counts.GetValueOrDefault(p, 0)).First();
        }

        return counts.GetValueOrDefault("WR", 0) <= counts.GetValueOrDefault("RB", 0) ? "WR" : "RB";
    }

    private static decimal ScoreLookAhead(IReadOnlyList<DraftLookAheadStep> steps, DraftStrategyState state)
    {
        var score = 0m;
        foreach (var step in steps)
        {
            var count = state.PositionalCounts.GetValueOrDefault(step.TargetPosition, 0);
            if (step.TargetPosition == "WR" && count < 3)
            {
                score += 0.8m;
            }
            else if (step.TargetPosition == "TE" && count < 1)
            {
                score += 0.8m;
            }
            else if (step.TargetPosition == "QB" && count < 1)
            {
                score += 0.6m;
            }
            else if (step.TargetPosition == "RB" && count < 2)
            {
                score += 0.6m;
            }
        }

        return score;
    }

    private static IReadOnlyList<DraftPickRecommendation> AssignRoles(
        IReadOnlyList<Evaluated> ranked,
        DraftStrategyState state)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        var picks = new List<DraftPickRecommendation>();
        var used = new HashSet<Guid>();

        // PRIMARY — best decision score.
        var primary = ranked[0];
        picks.Add(ToPick(primary, RecommendationRole.Primary));
        used.Add(primary.Input.Player.Id);

        // ALTERNATIVE — strong strategic alternative (prefer different position or preferred player).
        var alternative = ranked
            .Where(e => !used.Contains(e.Input.Player.Id))
            .OrderByDescending(e => AlternativeScore(e, primary, state))
            .FirstOrDefault();
        if (alternative is not null &&
            alternative.DecisionScore >= primary.DecisionScore - PreferenceOverrideGap * 2)
        {
            picks.Add(ToPick(alternative, RecommendationRole.Alternative));
            used.Add(alternative.Input.Player.Id);
        }

        // UPSIDE — higher ceiling / stash; must be meaningfully different.
        var upside = ranked
            .Where(e => !used.Contains(e.Input.Player.Id))
            .OrderByDescending(e =>
            {
                var bonus = 0m;
                if (e.Input.BaseRecommendation.PositionLabel.Equals("RB", StringComparison.OrdinalIgnoreCase)
                    && state.PositionalCounts.GetValueOrDefault("RB", 0) >= 2)
                {
                    bonus += 1.5m; // deep-RB sleepers belong in UPSIDE, not PRIMARY
                }

                return e.UpsideScore + bonus;
            })
            .FirstOrDefault();
        if (upside is not null &&
            picks.Count < MaxRecommendations &&
            (upside.UpsideScore >= primary.UpsideScore - 5m
             || upside.DecisionScore >= primary.DecisionScore - PreferenceOverrideGap * 2.5m))
        {
            picks.Add(ToPick(upside, RecommendationRole.Upside));
        }

        return picks;
    }

    private static decimal AlternativeScore(Evaluated candidate, Evaluated primary, DraftStrategyState state)
    {
        var score = candidate.DecisionScore;
        if (!string.Equals(
                candidate.Input.BaseRecommendation.PositionLabel,
                primary.Input.BaseRecommendation.PositionLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            score += 1.5m;
        }

        if (state.Plan.PreferredPlayers.Any(p =>
                DraftStrategyState.NamesMatch(p.PlayerName, candidate.Input.Player.FullName)))
        {
            score += 1.0m;
        }

        return score;
    }

    private static DraftPickRecommendation ToPick(Evaluated e, RecommendationRole role)
    {
        var baseRec = e.Input.BaseRecommendation;
        var player = new DraftRecommendation
        {
            PlayerId = baseRec.PlayerId,
            PlayerName = baseRec.PlayerName,
            PositionLabel = baseRec.PositionLabel,
            Team = baseRec.Team,
            ProjectedPoints = baseRec.ProjectedPoints,
            ValueOverReplacement = baseRec.ValueOverReplacement,
            BestPlayerAvailableRank = baseRec.BestPlayerAvailableRank,
            TeamFitRank = baseRec.TeamFitRank,
            Confidence = baseRec.Confidence,
            Reasoning = baseRec.Reasoning,
            Factors = baseRec.Factors,
            Category = role switch
            {
                RecommendationRole.Primary => RecommendationCategory.BestOverall,
                RecommendationRole.Alternative => RecommendationCategory.BestValue,
                RecommendationRole.Upside => RecommendationCategory.BestUpside,
                _ => RecommendationCategory.None
            },
            CategoryRationale = RecommendationRolePolicy.DisplayName(role)
        };

        return new DraftPickRecommendation
        {
            Player = player,
            Role = role,
            WhyBullets = e.WhyBullets,
            LookAhead = e.LookAhead,
            FitScore = Math.Round(e.FitScore, 2),
            StrategicScore = Math.Round(e.StrategicScore, 2),
            UrgencyScore = Math.Round(e.UrgencyScore, 2),
            UpsideScore = Math.Round(e.UpsideScore, 2)
        };
    }

    private sealed class Evaluated
    {
        public required CandidateInput Input { get; init; }
        public required decimal DecisionScore { get; init; }
        public required decimal FitScore { get; init; }
        public required decimal StrategicScore { get; init; }
        public required decimal UrgencyScore { get; init; }
        public required decimal UpsideScore { get; init; }
        public required IReadOnlyList<string> WhyBullets { get; init; }
        public required IReadOnlyList<DraftLookAheadStep> LookAhead { get; init; }
    }
}
