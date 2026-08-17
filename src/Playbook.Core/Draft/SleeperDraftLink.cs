using System.Text.RegularExpressions;

namespace Playbook.Core.Draft;

/// <summary>
/// Extracts a Sleeper draft id from whatever the user pastes.
///
/// Sleeper's draft/"Draftboard" URLs carry the draft id directly in the path, e.g.
///   https://sleeper.com/draft/nfl/1394906274017079296
///   sleeper.app/draft/nfl/1394906274017079296
/// and the app also shows the bare id. All of these resolve through the documented public
/// endpoints /v1/draft/{id} and /v1/draft/{id}/picks, so no scraping or authentication is needed.
///
/// Deliberately strict: a Sleeper id is a numeric snowflake. Accepting arbitrary text would let a
/// typo silently become a "draft not found" instead of an obvious "that isn't a draft link".
/// </summary>
public static partial class SleeperDraftLink
{
    /// <summary>Sleeper snowflake ids are long numeric strings; 12+ digits avoids matching stray numbers.</summary>
    [GeneratedRegex(@"(?<id>\d{12,25})", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>
    /// True when <paramref name="input"/> yields a plausible Sleeper draft id.
    /// Returns the id via <paramref name="draftId"/>.
    /// </summary>
    public static bool TryParse(string? input, out string draftId)
    {
        draftId = "";

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        // A bare id is the common case when someone copies from the Sleeper app.
        if (IsAllDigits(trimmed) && trimmed.Length is >= 12 and <= 25)
        {
            draftId = trimmed;
            return true;
        }

        // Otherwise pull the last numeric segment out of a URL. Last, not first, because a
        // Sleeper URL can carry a leading sport/season segment before the id.
        var matches = IdPattern().Matches(trimmed);
        if (matches.Count == 0)
        {
            return false;
        }

        draftId = matches[^1].Groups["id"].Value;
        return true;
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return value.Length > 0;
    }
}
