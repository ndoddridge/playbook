using System.Text.Json.Serialization;

namespace Playbook.Infrastructure.Leagues;

internal sealed class SleeperLeagueDto
{
    [JsonPropertyName("league_id")]
    public string? LeagueId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("sport")]
    public string? Sport { get; set; }

    [JsonPropertyName("total_rosters")]
    public int TotalRosters { get; set; }

    [JsonPropertyName("scoring_settings")]
    public Dictionary<string, double>? ScoringSettings { get; set; }

    [JsonPropertyName("roster_positions")]
    public List<string>? RosterPositions { get; set; }

    [JsonPropertyName("settings")]
    public SleeperLeagueSettingsDto? Settings { get; set; }
}

internal sealed class SleeperLeagueSettingsDto
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("num_teams")]
    public int NumTeams { get; set; }
}

internal sealed class SleeperRosterDto
{
    [JsonPropertyName("roster_id")]
    public int RosterId { get; set; }

    [JsonPropertyName("owner_id")]
    public string? OwnerId { get; set; }

    [JsonPropertyName("players")]
    public List<string>? Players { get; set; }

    [JsonPropertyName("starters")]
    public List<string>? Starters { get; set; }

    [JsonPropertyName("reserve")]
    public List<string>? Reserve { get; set; }

    [JsonPropertyName("taxi")]
    public List<string>? Taxi { get; set; }

    [JsonPropertyName("settings")]
    public SleeperRosterSettingsDto? Settings { get; set; }
}

internal sealed class SleeperRosterSettingsDto
{
    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }

    [JsonPropertyName("ties")]
    public int Ties { get; set; }

    [JsonPropertyName("fpts")]
    public double FantasyPoints { get; set; }

    [JsonPropertyName("fpts_decimal")]
    public double FantasyPointsDecimal { get; set; }
}

internal sealed class SleeperUserDto
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("metadata")]
    public SleeperUserMetadataDto? Metadata { get; set; }
}

internal sealed class SleeperUserMetadataDto
{
    [JsonPropertyName("team_name")]
    public string? TeamName { get; set; }
}

internal sealed class SleeperNflStateDto
{
    [JsonPropertyName("week")]
    public int Week { get; set; }

    [JsonPropertyName("display_week")]
    public int DisplayWeek { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }
}

internal sealed class SleeperDraftDto
{
    [JsonPropertyName("draft_id")]
    public string? DraftId { get; set; }

    [JsonPropertyName("league_id")]
    public string? LeagueId { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("start_time")]
    public long? StartTime { get; set; }

    [JsonPropertyName("draft_order")]
    public Dictionary<string, int>? DraftOrder { get; set; }

    [JsonPropertyName("settings")]
    public SleeperDraftSettingsDto? Settings { get; set; }

    [JsonPropertyName("slot_to_roster_id")]
    public Dictionary<string, int?>? SlotToRosterId { get; set; }

    [JsonPropertyName("metadata")]
    public SleeperDraftMetadataDto? Metadata { get; set; }
}

internal sealed class SleeperDraftMetadataDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("scoring_type")]
    public string? ScoringType { get; set; }

    [JsonPropertyName("league_type")]
    public string? LeagueType { get; set; }
}

internal sealed class SleeperDraftSettingsDto
{
    [JsonPropertyName("rounds")]
    public int Rounds { get; set; }

    [JsonPropertyName("teams")]
    public int Teams { get; set; }

    // Starting-lineup slot counts. Present on the draft itself, which is what lets a draft
    // attached by id describe its own roster shape without a league.
    [JsonPropertyName("slots_qb")]
    public int SlotsQb { get; set; }

    [JsonPropertyName("slots_rb")]
    public int SlotsRb { get; set; }

    [JsonPropertyName("slots_wr")]
    public int SlotsWr { get; set; }

    [JsonPropertyName("slots_te")]
    public int SlotsTe { get; set; }

    [JsonPropertyName("slots_flex")]
    public int SlotsFlex { get; set; }

    [JsonPropertyName("slots_super_flex")]
    public int SlotsSuperFlex { get; set; }

    [JsonPropertyName("slots_k")]
    public int SlotsK { get; set; }

    [JsonPropertyName("slots_def")]
    public int SlotsDef { get; set; }

    [JsonPropertyName("slots_bn")]
    public int SlotsBn { get; set; }

    /// <summary>Expand the slot counts into the same roster-position list a league publishes.</summary>
    internal IReadOnlyList<string> ToRosterPositions()
    {
        var positions = new List<string>();
        void Add(string label, int count)
        {
            for (var i = 0; i < count; i++)
            {
                positions.Add(label);
            }
        }

        Add("QB", SlotsQb);
        Add("RB", SlotsRb);
        Add("WR", SlotsWr);
        Add("TE", SlotsTe);
        Add("FLEX", SlotsFlex);
        Add("SUPER_FLEX", SlotsSuperFlex);
        Add("K", SlotsK);
        Add("DEF", SlotsDef);
        Add("BN", SlotsBn);
        return positions;
    }
}

internal sealed class SleeperDraftPickDto
{
    [JsonPropertyName("pick_no")]
    public int PickNo { get; set; }

    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("draft_slot")]
    public int DraftSlot { get; set; }

    [JsonPropertyName("roster_id")]
    public int? RosterId { get; set; }

    [JsonPropertyName("picked_by")]
    public string? PickedBy { get; set; }

    [JsonPropertyName("player_id")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("is_keeper")]
    public bool? IsKeeper { get; set; }
}
