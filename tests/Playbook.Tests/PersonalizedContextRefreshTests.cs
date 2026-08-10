using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Recommendations;
using Playbook.Core.Leagues;
using Playbook.Infrastructure.Recommendations;

namespace Playbook.Tests;

public class PersonalizedContextRefreshTests
{
    [Fact]
    public void Switching_League_Rebuilds_Recommendations_And_Projections()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var recommendations = provider.GetRequiredService<IRecommendationService>();
        var projections = provider.GetRequiredService<IProjectionService>();
        var players = provider.GetRequiredService<IPlayerService>();
        var chase = players.GetAllPlayers().First(p => p.FullName == "Ja'Marr Chase");

        var friends = leagues.GetAllLeagues().Single(l => l.Name == "Friends League");
        var dynasty = leagues.GetAllLeagues().Single(l => l.Name == "Dynasty League");
        Assert.Equal(ScoringType.Ppr, friends.ScoringType);
        Assert.Equal(ScoringType.HalfPpr, dynasty.ScoringType);

        leagues.SelectLeague(friends.Id);
        var friendsRecs = recommendations.GetTopRecommendations();
        var friendsChase = projections.GetProjection(chase.Id)!.ProjectedFantasyPoints;
        Assert.All(friendsRecs, r => Assert.Equal(friends.Id, r.LeagueId));
        Assert.All(friendsRecs, r => Assert.Equal(friends.SelectedRosterId, r.SelectedRosterId));
        Assert.Contains(friendsRecs, r => r.LeagueName == "Friends League");

        leagues.SelectLeague(dynasty.Id);
        var dynastyRecs = recommendations.GetTopRecommendations();
        var dynastyChase = projections.GetProjection(chase.Id)!.ProjectedFantasyPoints;

        Assert.All(dynastyRecs, r => Assert.Equal(dynasty.Id, r.LeagueId));
        Assert.DoesNotContain(dynastyRecs, r => r.LeagueId == friends.Id);
        Assert.NotEqual(
            string.Join("|", friendsRecs.Select(r => r.Id)),
            string.Join("|", dynastyRecs.Select(r => r.Id)));
        Assert.True(friendsChase > dynastyChase);
        Assert.Equal(dynasty.Id, projections.GetProjection(chase.Id)!.LeagueId);
    }

    [Fact]
    public void Changing_Owned_Team_Rebuilds_Recommendations()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var recommendations = provider.GetRequiredService<IRecommendationService>();

        var league = leagues.CurrentLeague!;
        Assert.Equal(1, league.SelectedRosterId);

        var before = recommendations.GetTopRecommendations();
        Assert.All(before, r => Assert.Equal(1, r.SelectedRosterId));

        Assert.True(leagues.SelectUserTeam(league.Id, 2));
        var after = recommendations.GetTopRecommendations();

        Assert.All(after, r => Assert.Equal(2, r.SelectedRosterId));
        Assert.DoesNotContain(after, r => r.SelectedRosterId == 1);
        Assert.NotEqual(
            string.Join("|", before.Select(r => r.Id)),
            string.Join("|", after.Select(r => r.Id)));
        Assert.Contains(after, r => r.TeamName != null && r.TeamName.Contains("Team 2", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectionService_Invalidate_Clears_Stale_Cache_Until_Reload()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var leagues = provider.GetRequiredService<ILeagueState>();
        var projections = provider.GetRequiredService<IProjectionService>();
        var players = provider.GetRequiredService<IPlayerService>();
        var chase = players.GetAllPlayers().First(p => p.FullName == "Ja'Marr Chase");

        var first = projections.GetProjection(chase.Id);
        Assert.NotNull(first);

        projections.Invalidate();
        leagues.SelectLeague(leagues.GetAllLeagues().Single(l => l.Name == "Work League").Id);
        var second = projections.GetProjection(chase.Id);

        Assert.NotNull(second);
        Assert.Equal(ScoringType.Standard, second!.ScoringFormat);
        Assert.NotEqual(first!.LeagueId, second.LeagueId);
    }

    [Fact]
    public void Roster_Scoped_Recommendations_Use_Owned_Team_Players()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var realState = provider.GetRequiredService<ILeagueState>();
        var projections = provider.GetRequiredService<IProjectionService>();
        var players = provider.GetRequiredService<IPlayerService>();

        var league = realState.CurrentLeague!;
        var teamOnePlayers = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111109"), // Chase
            Guid.Parse("11111111-1111-1111-1111-111111111104")  // Irving
        };
        var teamTwoPlayers = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111103"), // Mahomes
            Guid.Parse("11111111-1111-1111-1111-111111111110")  // Lamb
        };

        var fake = new FakeLeagueState(league, teamOnePlayers, teamTwoPlayers);
        var recs = new MockRecommendationService(fake, projections, players);

        fake.SelectUserTeam(league.Id, 1);
        var teamOneRecs = recs.GetTopRecommendations();
        Assert.All(teamOneRecs, r => Assert.Equal(1, r.SelectedRosterId));
        Assert.Contains(teamOneRecs, r => r.RelatedPlayerId == teamOnePlayers[0] || r.RelatedPlayerId == teamOnePlayers[1]);
        Assert.DoesNotContain(teamOneRecs, r => r.RelatedPlayerId == teamTwoPlayers[0]);

        fake.SelectUserTeam(league.Id, 2);
        var teamTwoRecs = recs.GetTopRecommendations();
        Assert.All(teamTwoRecs, r => Assert.Equal(2, r.SelectedRosterId));
        Assert.Contains(teamTwoRecs, r => r.RelatedPlayerId == teamTwoPlayers[0] || r.RelatedPlayerId == teamTwoPlayers[1]);
        Assert.DoesNotContain(teamTwoRecs, r => r.SelectedRosterId == 1);
        Assert.NotEqual(
            string.Join("|", teamOneRecs.Select(r => r.Id)),
            string.Join("|", teamTwoRecs.Select(r => r.Id)));
    }

    private sealed class FakeLeagueState : ILeagueState
    {
        private League _league;
        private readonly Dictionary<int, FantasyTeam> _teams;

        public FakeLeagueState(League league, IReadOnlyList<Guid> teamOne, IReadOnlyList<Guid> teamTwo)
        {
            _league = Clone(league, 1);
            _teams = new Dictionary<int, FantasyTeam>
            {
                [1] = new FantasyTeam
                {
                    LeagueId = league.Id,
                    RosterId = 1,
                    DisplayName = "Owner One",
                    TeamName = "Alpha",
                    PlayerIds = teamOne,
                    StarterIds = teamOne.Take(1).ToList(),
                    ExternalPlayerIds = []
                },
                [2] = new FantasyTeam
                {
                    LeagueId = league.Id,
                    RosterId = 2,
                    DisplayName = "Owner Two",
                    TeamName = "Beta",
                    PlayerIds = teamTwo,
                    StarterIds = teamTwo.Take(1).ToList(),
                    ExternalPlayerIds = []
                }
            };
        }

        public League? CurrentLeague => _league;

        public FantasyTeam? CurrentUserTeam =>
            _league.SelectedRosterId is int id && _teams.TryGetValue(id, out var team) ? team : null;

        public event Action? Changed;

        public IReadOnlyList<League> GetAllLeagues() => [_league];

        public League? GetCurrentLeague() => _league;

        public void SelectLeague(Guid leagueId) { }

        public IReadOnlyList<FantasyTeam> GetTeams(Guid leagueId) => _teams.Values.ToList();

        public IReadOnlyList<FantasyTeam> GetCurrentTeams() => _teams.Values.ToList();

        public FantasyTeam? FindTeamForPlayer(Guid playerId) =>
            _teams.Values.FirstOrDefault(t => t.PlayerIds.Contains(playerId));

        public FantasyTeam? GetUserTeam(Guid leagueId) => CurrentUserTeam;

        public FantasyTeam? GetCurrentUserTeam() => CurrentUserTeam;

        public bool SelectUserTeam(Guid leagueId, int rosterId)
        {
            if (!_teams.ContainsKey(rosterId))
            {
                return false;
            }

            _league = Clone(_league, rosterId);
            Changed?.Invoke();
            return true;
        }

        public Task<LeagueConnectResult> ConnectSleeperLeagueAsync(
            string sleeperLeagueId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LeagueConnectResult.Fail("not used"));

        private static League Clone(League league, int rosterId) =>
            new()
            {
                Id = league.Id,
                Name = league.Name,
                Platform = league.Platform,
                LeagueType = league.LeagueType,
                ScoringType = league.ScoringType,
                NumberOfTeams = league.NumberOfTeams,
                CurrentWeek = league.CurrentWeek,
                Season = league.Season,
                IsActive = league.IsActive,
                ExternalId = league.ExternalId,
                DataSource = league.DataSource,
                ReceptionPoints = league.ReceptionPoints,
                SelectedRosterId = rosterId
            };
    }
}
