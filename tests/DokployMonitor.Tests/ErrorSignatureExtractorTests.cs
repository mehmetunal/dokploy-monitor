using DokployMonitor.Web.Services;

namespace DokployMonitor.Tests;

public class ErrorSignatureExtractorTests
{
    [Fact]
    public void Same_root_cause_with_different_ids_produces_one_signature()
    {
        var first = ErrorSignatureExtractor.Extract(
            "Error: container 9f2a1b3c4d5e6f70 exited with code 137 at 2026-07-25T10:00:00Z");
        var second = ErrorSignatureExtractor.Extract(
            "Error: container 1a2b3c4d5e6f7890 exited with code 143 at 2026-07-24T22:13:07Z");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Hash, second.Hash);
    }

    [Fact]
    public void Different_root_causes_produce_different_signatures()
    {
        var first = ErrorSignatureExtractor.Extract("npm ERR! code ELIFECYCLE");
        var second = ErrorSignatureExtractor.Extract("dotnet restore failed: NU1101");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Ansi_codes_are_stripped_before_grouping()
    {
        var withColor = ErrorSignatureExtractor.Extract("\u001b[31mError:\u001b[0m build failed");
        var plain = ErrorSignatureExtractor.Extract("Error: build failed");

        Assert.NotNull(withColor);
        Assert.NotNull(plain);
        Assert.Equal(plain.Hash, withColor.Hash);
        Assert.DoesNotContain("\u001b", withColor.NormalizedMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_first_meaningful_line_is_used()
    {
        var signature = ErrorSignatureExtractor.Extract("\n\n  Build failed  \nstack line 1\nstack line 2");

        Assert.NotNull(signature);
        Assert.Equal("Build failed", signature.NormalizedMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_messages_produce_no_signature(string? message) =>
        Assert.Null(ErrorSignatureExtractor.Extract(message));
}
