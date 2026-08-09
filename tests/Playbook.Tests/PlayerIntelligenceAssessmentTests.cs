using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Injuries.Models;

namespace Playbook.Tests;

public class PlayerIntelligenceAssessmentTests
{
    [Fact]
    public void Assessment_Composes_Existing_Services_Without_Fabricating()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var assessments = provider.GetRequiredService<IPlayerIntelligenceAssessmentService>();
        var players = provider.GetRequiredService<IPlayerService>();

        var player = players.GetAllPlayers().First();
        var assessment = assessments.GetAssessment(player.Id);

        Assert.Equal(player.Id, assessment.PlayerId);
        Assert.False(string.IsNullOrWhiteSpace(assessment.OutlookLabel));
        Assert.False(string.IsNullOrWhiteSpace(assessment.Headline));
        Assert.InRange(assessment.AssessmentConfidence, 5, 95);
        Assert.DoesNotContain(assessment.PositiveFactors, f => string.IsNullOrWhiteSpace(f.Text));
        Assert.DoesNotContain(assessment.NegativeFactors, f => string.IsNullOrWhiteSpace(f.Text));
    }

    [Fact]
    public void Assessment_Marks_Unconfirmed_Items_Distinctly()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var assessments = provider.GetRequiredService<IPlayerIntelligenceAssessmentService>();
        var players = provider.GetRequiredService<IPlayerService>();

        foreach (var player in players.GetAllPlayers().Take(25))
        {
            var assessment = assessments.GetAssessment(player.Id);
            foreach (var item in assessment.RecentIntelligence.Where(i => !i.IsConfirmed))
            {
                Assert.Contains(item.VerificationLabel, new[] { "Unconfirmed", "Reported" });
            }

            if (assessment.InjuryProfile?.UnconfirmedSignals.Count > 0)
            {
                Assert.Contains(
                    assessment.NegativeFactors,
                    f => f.Text.Contains("Unconfirmed", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Assessment_Does_Not_Treat_Missing_Injury_As_Positive_Fabrication()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var assessments = provider.GetRequiredService<IPlayerIntelligenceAssessmentService>();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var players = provider.GetRequiredService<IPlayerService>();

        var withUnavailable = players.GetAllPlayers()
            .Select(p => (Player: p, Injury: injuries.GetPlayerInjuryProfile(p.Id)))
            .FirstOrDefault(x => x.Injury?.CurrentDataStatus == CurrentInjuryDataStatus.Unavailable);

        if (withUnavailable.Player is null)
        {
            var any = assessments.GetAssessment(players.GetAllPlayers().First().Id);
            Assert.NotNull(any.UnavailableSignals);
            return;
        }

        var assessment = assessments.GetAssessment(withUnavailable.Player.Id);
        Assert.Contains(
            assessment.UnavailableSignals,
            s => s.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            assessment.PositiveFactors,
            f => f.Text.Contains("Healthy", StringComparison.OrdinalIgnoreCase) &&
                 assessment.HealthStatusLabel.Contains("Unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assessment_Includes_Projection_When_Available()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var assessments = provider.GetRequiredService<IPlayerIntelligenceAssessmentService>();
        var players = provider.GetRequiredService<IPlayerService>();

        var withProjection = players.GetAllPlayers()
            .Select(p => assessments.GetAssessment(p.Id))
            .FirstOrDefault(a => a.Projection is not null);

        Assert.NotNull(withProjection);
        Assert.False(string.IsNullOrWhiteSpace(withProjection!.ProjectionSummary));
        Assert.Contains(withProjection.DetailSections, s => s.Title == "Projection");
    }
}
