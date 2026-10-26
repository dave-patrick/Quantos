using Quantos.Core.Security;

namespace Quantos.Tests;

public sealed class LocalOriginTests
{
    [Theory]
    [InlineData("http://127.0.0.1:47821/")]
    [InlineData("http://127.0.0.1:47821/note/id?query=x#heading")]
    public void Only_the_expected_origin_can_reach_the_native_bridge(string source) =>
        Assert.True(LocalOrigin.Matches(source, "http://127.0.0.1:47821/"));

    [Theory]
    [InlineData("http://127.0.0.1:47821@evil.example/")]
    [InlineData("http://127.0.0.1:47821.evil.example/")]
    [InlineData("http://127.0.0.1:47822/")]
    [InlineData("https://127.0.0.1:47821/")]
    [InlineData("http://localhost:47821/")]
    [InlineData("http://person@127.0.0.1:47821/")]
    [InlineData("http://127.0.0.1:47821\\@evil.example/")]
    [InlineData("http://127.0.0.1:47821/\r\nignored")]
    [InlineData("about:blank")]
    [InlineData("/relative")]
    [InlineData(null)]
    public void Misleading_or_other_origins_are_denied(string? source) =>
        Assert.False(LocalOrigin.Matches(source, "http://127.0.0.1:47821/"));
}
