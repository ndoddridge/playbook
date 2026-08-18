using Playbook.Core.Draft;

namespace Playbook.Application.Draft;

/// <summary>
/// Extracts draft picks from an uploaded screenshot/photo via a vision model. Returns raw
/// extracted text only — never a resolved player/owner identity, and never a guessed pick.
/// Ambiguous or unreadable rows are flagged, not fabricated.
/// </summary>
public interface IDraftImageIngestionService
{
    Task<DraftImageParseResult> ParseDraftScreenshotAsync(
        byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default);
}
