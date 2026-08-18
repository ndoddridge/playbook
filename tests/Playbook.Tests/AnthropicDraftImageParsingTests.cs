using Playbook.Infrastructure.Draft;

namespace Playbook.Tests;

/// <summary>
/// Pure parsing of the Anthropic Messages API response body — no HTTP. Malformed or unexpected
/// shapes must always come back as <c>Unparseable = true</c> with a warning, never a thrown
/// exception and never a fabricated pick.
/// </summary>
public class AnthropicDraftImageParsingTests
{
    [Fact]
    public void Parses_A_Well_Formed_Structured_Output_Response()
    {
        var body = ResponseWithText("""
            {"unparseable":false,"warnings":[],"picks":[
              {"pickNumber":1,"round":1,"ownerText":"Team A","playerText":"Player One","positionText":"RB","isAmbiguous":false,"ambiguityReason":null}
            ]}
            """);

        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse(body);

        Assert.False(result.Unparseable);
        var pick = Assert.Single(result.Picks);
        Assert.Equal(1, pick.PickNumber);
        Assert.Equal("Team A", pick.OwnerText);
        Assert.Equal("Player One", pick.PlayerText);
        Assert.False(pick.IsAmbiguous);
    }

    [Fact]
    public void Skips_A_Leading_Thinking_Block_To_Find_The_Structured_Text_Block()
    {
        var body = """
            {"content":[
              {"type":"thinking","thinking":"reasoning about the image"},
              {"type":"text","text":"{\"unparseable\":false,\"warnings\":[],\"picks\":[]}"}
            ]}
            """;

        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse(body);

        Assert.False(result.Unparseable);
        Assert.Empty(result.Picks);
    }

    [Fact]
    public void Flags_A_Pick_As_Ambiguous_Instead_Of_Guessing()
    {
        var body = ResponseWithText("""
            {"unparseable":false,"warnings":[],"picks":[
              {"pickNumber":3,"round":1,"ownerText":"Team C","playerText":null,"positionText":null,"isAmbiguous":true,"ambiguityReason":"Player name is obscured by a graphic."}
            ]}
            """);

        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse(body);

        var pick = Assert.Single(result.Picks);
        Assert.True(pick.IsAmbiguous);
        Assert.Equal("Player name is obscured by a graphic.", pick.AmbiguityReason);
        Assert.Null(pick.PlayerText);
    }

    [Fact]
    public void Reports_Unparseable_When_The_Model_Says_The_Image_Is_Not_A_Draft_Board()
    {
        var body = ResponseWithText("""{"unparseable":true,"warnings":["This looks like a photo of a dog."],"picks":[]}""");

        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse(body);

        Assert.True(result.Unparseable);
        Assert.Contains(result.Warnings, w => w.Contains("dog", StringComparison.Ordinal));
        Assert.Empty(result.Picks);
    }

    [Fact]
    public void Reports_Unparseable_On_A_Refusal_Rather_Than_Throwing()
    {
        var body = """{"stop_reason":"refusal","content":[]}""";

        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse(body);

        Assert.True(result.Unparseable);
        Assert.NotEmpty(result.Warnings);
        Assert.Empty(result.Picks);
    }

    [Fact]
    public void Reports_Unparseable_When_The_Inner_Text_Is_Not_Valid_Json()
    {
        var body = ResponseWithText("this is not json");

        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse(body);

        Assert.True(result.Unparseable);
    }

    [Fact]
    public void Reports_Unparseable_On_A_Malformed_Envelope_Rather_Than_Throwing()
    {
        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse("{ not json at all");

        Assert.True(result.Unparseable);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Reports_Unparseable_When_There_Is_No_Content_Array()
    {
        var result = AnthropicDraftImageIngestionService.ParseAnthropicResponse("""{"id":"msg_1"}""");

        Assert.True(result.Unparseable);
    }

    private static string ResponseWithText(string innerJson)
    {
        var escaped = innerJson.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
        return $$"""{"id":"msg_1","content":[{"type":"text","text":"{{escaped}}"}]}""";
    }
}
