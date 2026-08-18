using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Draft;
using Playbook.Core.Draft;

namespace Playbook.Infrastructure.Draft;

/// <summary>
/// Parses draft screenshots via the Anthropic Messages API (vision + structured outputs).
/// Plain HTTP, no SDK — matches how this codebase calls every other external API
/// (<c>SleeperLeagueClient</c>, <c>LivePropLineProvider</c>). Requires
/// <see cref="AnthropicVisionOptions.ApiKey"/>; when unset, callers see a clear
/// <see cref="InvalidOperationException"/> naming the env var rather than a startup crash.
/// </summary>
public sealed class AnthropicDraftImageIngestionService : IDraftImageIngestionService
{
    public const string HttpClientName = "AnthropicVision";

    private const string Prompt =
        "This image is a fantasy football draft board or draft results screenshot. Extract every " +
        "visible pick as an array. For each pick capture the pick number, round, the owner/team " +
        "text exactly as shown, the player name text exactly as shown, and the position if visible. " +
        "If any field is unclear, illegible, or ambiguous, set isAmbiguous to true on that pick and " +
        "explain why in ambiguityReason rather than guessing a value. If the image does not appear " +
        "to be a draft board at all, set unparseable to true and leave picks empty.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicVisionOptions _options;
    private readonly ILogger<AnthropicDraftImageIngestionService> _logger;

    public AnthropicDraftImageIngestionService(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicVisionOptions> options,
        ILogger<AnthropicDraftImageIngestionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DraftImageParseResult> ParseDraftScreenshotAsync(
        byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "DraftImageIngestion__ApiKey is empty. Configure an Anthropic API key to enable draft screenshot import.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var request = BuildRequest(_options.Model, imageBytes, mediaType);

        using var response = await client.PostAsJsonAsync(string.Empty, request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic vision request failed ({Status}): {Body}", (int)response.StatusCode, body);
            return new DraftImageParseResult([], [$"Vision request failed ({(int)response.StatusCode})."], true);
        }

        return ParseAnthropicResponse(body);
    }

    private static JsonObject BuildRequest(string model, byte[] imageBytes, string mediaType) => new()
    {
        ["model"] = model,
        ["max_tokens"] = 8000,
        ["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = mediaType,
                            ["data"] = Convert.ToBase64String(imageBytes)
                        }
                    },
                    new JsonObject { ["type"] = "text", ["text"] = Prompt }
                }
            }
        },
        ["output_config"] = new JsonObject
        {
            ["effort"] = "low",
            ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = PicksSchema() }
        }
    };

    private static JsonObject PicksSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["unparseable"] = new JsonObject { ["type"] = "boolean" },
            ["warnings"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["picks"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["pickNumber"] = new JsonObject { ["type"] = new JsonArray { "integer", "null" } },
                        ["round"] = new JsonObject { ["type"] = new JsonArray { "integer", "null" } },
                        ["ownerText"] = new JsonObject { ["type"] = new JsonArray { "string", "null" } },
                        ["playerText"] = new JsonObject { ["type"] = new JsonArray { "string", "null" } },
                        ["positionText"] = new JsonObject { ["type"] = new JsonArray { "string", "null" } },
                        ["isAmbiguous"] = new JsonObject { ["type"] = "boolean" },
                        ["ambiguityReason"] = new JsonObject { ["type"] = new JsonArray { "string", "null" } }
                    },
                    ["required"] = new JsonArray
                    {
                        "pickNumber", "round", "ownerText", "playerText", "positionText", "isAmbiguous", "ambiguityReason"
                    },
                    ["additionalProperties"] = false
                }
            }
        },
        ["required"] = new JsonArray { "unparseable", "warnings", "picks" },
        ["additionalProperties"] = false
    };

    /// <summary>
    /// Pure — no I/O. Parses the raw Anthropic Messages API response body. Never throws on
    /// malformed input; an unexpected shape becomes <c>Unparseable = true</c> with a warning
    /// instead of a fabricated pick.
    /// </summary>
    internal static DraftImageParseResult ParseAnthropicResponse(string rawJsonResponseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJsonResponseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("stop_reason", out var stopReasonEl) &&
                stopReasonEl.GetString() == "refusal")
            {
                return new DraftImageParseResult([], ["The vision model declined to process this image."], true);
            }

            if (!root.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.Array)
            {
                return new DraftImageParseResult([], ["Unexpected response from the vision model."], true);
            }

            string? text = null;
            foreach (var block in contentEl.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text" &&
                    block.TryGetProperty("text", out var textEl))
                {
                    text = textEl.GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new DraftImageParseResult([], ["The vision model returned no readable text content."], true);
            }

            using var payloadDoc = JsonDocument.Parse(text);
            var payload = payloadDoc.RootElement;

            var unparseable = payload.TryGetProperty("unparseable", out var u) && u.ValueKind == JsonValueKind.True;
            var warnings = payload.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array
                ? w.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList()
                : [];

            var picks = new List<DraftImageParsedPick>();
            if (payload.TryGetProperty("picks", out var picksEl) && picksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in picksEl.EnumerateArray())
                {
                    picks.Add(new DraftImageParsedPick(
                        GetNullableInt(p, "pickNumber"),
                        GetNullableInt(p, "round"),
                        GetNullableString(p, "ownerText"),
                        GetNullableString(p, "playerText"),
                        GetNullableString(p, "positionText"),
                        p.TryGetProperty("isAmbiguous", out var amb) && amb.ValueKind == JsonValueKind.True,
                        GetNullableString(p, "ambiguityReason")));
                }
            }

            return new DraftImageParseResult(picks, warnings, unparseable);
        }
        catch (JsonException)
        {
            return new DraftImageParseResult([], ["The vision model's response was not valid JSON."], true);
        }
    }

    private static int? GetNullableInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

    private static string? GetNullableString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
