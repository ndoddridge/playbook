using System.Security.Claims;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Predictions;
using Playbook.Core.Predictions.Models;
using Playbook.Infrastructure.Access;
using Playbook.Web.Features.Access;
using Playbook.Web.Features.QuickPicks.Services;

namespace Playbook.Tests.Access;

public class QuickPicksBoardIsolationTests
{
    private static readonly NflWeekRef Week1 = new(2026, NflSeasonPhase.RegularSeason, 1);
    private static readonly NflWeekRef Week5 = new(2026, NflSeasonPhase.RegularSeason, 5);

    [Fact]
    public void One_Users_Week_Choice_Does_Not_Change_Another_Users_Board()
    {
        var shared = new FakeQuickPicksService(Week1);
        var alice = new QuickPicksBoard(shared);
        var bob = new QuickPicksBoard(shared);

        Assert.True(alice.TrySelectWeek(Week5));

        // Alice moved to week 5; Bob (who never chose) still follows the engine default... until he
        // picks his own, after which each board keeps reading its own week.
        Assert.Equal(Week5, alice.SelectedWeek);
        Assert.True(bob.TrySelectWeek(Week1));
        Assert.Equal(Week1, bob.SelectedWeek);
        Assert.Equal(Week5, alice.SelectedWeek);
        Assert.Equal(Week5, alice.CanonicalWeeks.Single());
        Assert.Equal(Week1, bob.CanonicalWeeks.Single());
    }

    [Fact]
    public void Signed_In_Principal_Carries_A_Hashed_User_Id_Not_The_Raw_Key()
    {
        var principal = AccessSetup.BuildPrincipal("  My-Key-1 ", "inst-1", DateTimeOffset.UtcNow);

        Assert.Equal(LicenseIdentity.UserIdFor("my-key-1"), principal.FindFirstValue(AccessClaims.UserId));
        Assert.Equal("My-Key-1", principal.FindFirstValue(AccessClaims.LicenseKey));
        Assert.Equal("inst-1", principal.FindFirstValue(AccessClaims.InstanceId));
        Assert.True(principal.Identity!.IsAuthenticated);
    }

    private sealed class FakeQuickPicksService(NflWeekRef initial) : IQuickPicksService
    {
        private NflWeekRef _selected = initial;

        public NflWeekRef? SelectedWeek => _selected;
        public IReadOnlyList<NflSlate> AvailableSlates => [];
        public IReadOnlyList<NflWeekRef> AvailableWeeks => [];
        // Exposes the engine's currently selected week so the test can see whose week is applied.
        public IReadOnlyList<NflWeekRef> CanonicalWeeks => [_selected];
        public NflSeasonContext? SeasonContext => null;
        public IReadOnlyList<Prediction> GetAllPredictions() => [];
        public IReadOnlyList<Prediction> GetTopPicks(int count = 5) => [];
        public IReadOnlyList<Prediction> GetWatchPicks(int count = 8, int topCount = 5) => [];
        public IReadOnlyList<Prediction> GetSlatePredictions() => [];
        public IReadOnlyList<FootballEvent> GetUpcomingEvents() => [];
        public (int GameMarketLines, int PlayerPropLines) GetSlateMarketCounts() => (0, 0);
        public IReadOnlyList<GameMarketAnalysis> GetGameMarketAnalyses() => [];

        public bool TrySelectWeek(NflWeekRef week)
        {
            _selected = week;
            return true;
        }

        public void Refresh()
        {
        }
    }
}
