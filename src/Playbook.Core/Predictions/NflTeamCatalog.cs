namespace Playbook.Core.Predictions;

/// <summary>
/// Canonical NFL team identities + search aliases (city, nickname, abbreviations).
/// Extensible single source for Quick Picks search normalization.
/// </summary>
public static class NflTeamCatalog
{
    public sealed record TeamIdentity(
        string Abbreviation,
        string City,
        string Nickname,
        IReadOnlyList<string> Aliases);

    public static IReadOnlyList<TeamIdentity> Teams { get; } =
    [
        Team("ARI", "Arizona", "Cardinals", "ARZ", "Arizona Cardinals"),
        Team("ATL", "Atlanta", "Falcons", "Atlanta Falcons"),
        Team("BAL", "Baltimore", "Ravens", "Baltimore Ravens"),
        Team("BUF", "Buffalo", "Bills", "Buffalo Bills"),
        Team("CAR", "Carolina", "Panthers", "Carolina Panthers"),
        Team("CHI", "Chicago", "Bears", "Chicago Bears"),
        Team("CIN", "Cincinnati", "Bengals", "Cincy", "Cincinnati Bengals"),
        Team("CLE", "Cleveland", "Browns", "Cleveland Browns"),
        Team("DAL", "Dallas", "Cowboys", "Dallas Cowboys"),
        Team("DEN", "Denver", "Broncos", "Denver Broncos"),
        Team("DET", "Detroit", "Lions", "Detroit Lions"),
        Team("GB", "Green Bay", "Packers", "GNB", "Green Bay Packers"),
        Team("HOU", "Houston", "Texans", "Houston Texans"),
        Team("IND", "Indianapolis", "Colts", "Indianapolis Colts"),
        Team("JAX", "Jacksonville", "Jaguars", "JAC", "Jags", "Jacksonville Jaguars"),
        Team("KC", "Kansas City", "Chiefs", "KAN", "Kansas City Chiefs"),
        Team("LV", "Las Vegas", "Raiders", "LVR", "Oakland", "Oakland Raiders", "Las Vegas Raiders"),
        Team("LAC", "Los Angeles", "Chargers", "SD", "San Diego", "San Diego Chargers", "LA Chargers", "Los Angeles Chargers"),
        Team("LAR", "Los Angeles", "Rams", "LA", "St Louis", "St. Louis", "LA Rams", "Los Angeles Rams"),
        Team("MIA", "Miami", "Dolphins", "Miami Dolphins"),
        Team("MIN", "Minnesota", "Vikings", "Minnesota Vikings"),
        Team("NE", "New England", "Patriots", "NEP", "New England Patriots"),
        Team("NO", "New Orleans", "Saints", "NOR", "New Orleans Saints"),
        Team("NYG", "New York", "Giants", "New York Giants", "NY Giants"),
        Team("NYJ", "New York", "Jets", "New York Jets", "NY Jets"),
        Team("PHI", "Philadelphia", "Eagles", "Philadelphia Eagles"),
        Team("PIT", "Pittsburgh", "Steelers", "Pittsburgh Steelers"),
        Team("SF", "San Francisco", "49ers", "Niners", "SFO", "San Francisco 49ers"),
        Team("SEA", "Seattle", "Seahawks", "Seattle Seahawks"),
        Team("TB", "Tampa Bay", "Buccaneers", "Tampa", "Bucs", "TAM", "Tampa Bay Buccaneers"),
        Team("TEN", "Tennessee", "Titans", "Tennessee Titans"),
        Team("WAS", "Washington", "Commanders", "WSH", "Washington Commanders", "Football Team", "Redskins")
    ];

    private static readonly Dictionary<string, string> AliasToAbbreviation =
        BuildAliasIndex();

    /// <summary>
    /// Resolve a free-text query to canonical abbreviations (empty if not a team query).
    /// </summary>
    public static IReadOnlyList<string> ResolveAbbreviations(string? query)
    {
        var key = Normalize(query);
        if (key.Length == 0)
        {
            return [];
        }

        if (AliasToAbbreviation.TryGetValue(key, out var exact))
        {
            return [exact];
        }

        // Partial nickname/city matches (e.g. "bay" is too short — require length >= 4 for contains).
        if (key.Length < 4)
        {
            return [];
        }

        return Teams
            .Where(t => t.Aliases.Any(a => Normalize(a).Contains(key, StringComparison.Ordinal))
                        || Normalize(t.City).Contains(key, StringComparison.Ordinal)
                        || Normalize(t.Nickname).Contains(key, StringComparison.Ordinal))
            .Select(t => t.Abbreviation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>True when <paramref name="teamToken"/> (abbr or name) matches the query via aliases.</summary>
    public static bool TeamMatchesQuery(string? teamToken, string? query)
    {
        if (string.IsNullOrWhiteSpace(teamToken) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        if (teamToken.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(teamToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var resolved = ResolveAbbreviations(query);
        if (resolved.Count == 0)
        {
            return false;
        }

        if (resolved.Any(a => string.Equals(a, teamToken, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // teamToken might itself be a city/nickname
        var tokenAbbr = ResolveAbbreviations(teamToken);
        return tokenAbbr.Count > 0 && resolved.Any(r =>
            tokenAbbr.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var chars = value.Trim().ToLowerInvariant()
            .Where(c => !char.IsWhiteSpace(c) && c is not '.' and not '\'' and not '-')
            .ToArray();
        return new string(chars);
    }

    private static TeamIdentity Team(
        string abbreviation,
        string city,
        string nickname,
        params string[] extraAliases)
    {
        var aliases = new List<string>
        {
            abbreviation,
            city,
            nickname,
            $"{city} {nickname}"
        };
        aliases.AddRange(extraAliases);
        return new TeamIdentity(abbreviation, city, nickname, aliases);
    }

    private static Dictionary<string, string> BuildAliasIndex()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var team in Teams)
        {
            foreach (var alias in team.Aliases.Append(team.Abbreviation).Append(team.City).Append(team.Nickname))
            {
                var key = Normalize(alias);
                if (key.Length == 0)
                {
                    continue;
                }

                // Prefer first registration; "LA" maps to LAR intentionally via Teams order... 
                // LAR and LAC both have LA-related aliases — keep explicit abbreviations unique.
                if (key is "la" or "losangeles")
                {
                    // Ambiguous — skip exact index; ResolveAbbreviations handles via contains on longer tokens.
                    continue;
                }

                map.TryAdd(key, team.Abbreviation);
            }
        }

        return map;
    }
}
