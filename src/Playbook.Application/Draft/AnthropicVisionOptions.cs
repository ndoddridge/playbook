namespace Playbook.Application.Draft;

/// <summary>
/// Config for the Anthropic Vision adapter that parses draft screenshots
/// (<see cref="IDraftImageIngestionService"/>). Supply the key via
/// env <c>DraftImageIngestion__ApiKey</c> (preferred) or config/user-secret
/// <c>DraftImageIngestion:ApiKey</c> — never commit a real key. When empty, image
/// ingestion is unavailable; the UI hides the upload panel rather than the service
/// crashing at startup.
/// </summary>
public sealed class AnthropicVisionOptions
{
    public const string SectionName = "DraftImageIngestion";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-opus-5";

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public int TimeoutSeconds { get; set; } = 30;
}
