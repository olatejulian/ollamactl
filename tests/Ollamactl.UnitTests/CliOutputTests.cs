using System.Globalization;
using System.Text.Json;
using Ollamactl.Cli.Runtime;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class CliOutputTests
{
    [Theory]
    [InlineData(-1, "0 B")]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1280, "1.25 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(1048576, "1 MiB")]
    [InlineData(1073741824, "1 GiB")]
    [InlineData(1099511627776, "1 TiB")]
    [InlineData(1125899906842624, "1024 TiB")]
    public void FormatBytesUsesBinaryUnitsAndClampsNegativeValues(long bytes, string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal(expected, CliOutput.FormatBytes(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void WriteErrorJsonWritesMachineReadableErrorOnlyToStandardError()
    {
        using var console = new TestCliConsole();
        var output = new CliOutput(console);

        output.WriteError(ExitCodes.Unavailable, "server is unavailable", OutputFormat.Json);

        Assert.Equal(string.Empty, console.Output);
        using var document = JsonDocument.Parse(console.Error);
        Assert.Equal(ExitCodes.Unavailable, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("server is unavailable", document.RootElement.GetProperty("error").GetString());
    }
}
