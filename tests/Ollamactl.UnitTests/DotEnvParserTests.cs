using Ollamactl.Application.Configuration;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class DotEnvParserTests
{
    [Fact]
    public void ParseHandlesCommentsQuotesEscapesAndDuplicateKeys()
    {
        const string contents = """
            # full-line comment

            PLAIN = value
            INLINE=value with spaces   # trailing comment
            HASH=abc#not-a-comment
            EMPTY=
            DOUBLE="line\nnext\t\"quoted\"\\tail" # comment
            SINGLE='literal\n#value'
            EQUALS=left=right
            duplicate=first
            DUPLICATE=second
            """;

        var values = DotEnvParser.Parse(contents);

        Assert.Equal(8, values.Count);
        Assert.Equal("value", values["PLAIN"]);
        Assert.Equal("value with spaces", values["INLINE"]);
        Assert.Equal("abc#not-a-comment", values["HASH"]);
        Assert.Equal(string.Empty, values["EMPTY"]);
        Assert.Equal("line\nnext\t\"quoted\"\\tail", values["DOUBLE"]);
        Assert.Equal("literal\\n#value", values["SINGLE"]);
        Assert.Equal("left=right", values["EQUALS"]);
        Assert.Equal("second", values["duplicate"]);
    }

    [Theory]
    [InlineData("MISSING_SEPARATOR", "Invalid environment entry")]
    [InlineData("=value", "Invalid environment entry")]
    [InlineData("9NAME=value", "Invalid environment variable name")]
    [InlineData("BAD-NAME=value", "Invalid environment variable name")]
    [InlineData("QUOTED=\"unterminated", "Unterminated quoted value")]
    [InlineData("QUOTED=\"ok\" trailing", "Unexpected content after quoted value")]
    public void ParseRejectsMalformedEntries(string contents, string expectedMessage)
    {
        var exception = Assert.Throws<FormatException>(() => DotEnvParser.Parse(contents));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Contains("line 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseReportsPhysicalLineNumber()
    {
        const string contents = "# comment\n\nGOOD=value\nbroken";

        var exception = Assert.Throws<FormatException>(() => DotEnvParser.Parse(contents));

        Assert.Contains("line 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseThrowsForNullContents()
    {
        Assert.Throws<ArgumentNullException>(() => DotEnvParser.Parse(null!));
    }
}
