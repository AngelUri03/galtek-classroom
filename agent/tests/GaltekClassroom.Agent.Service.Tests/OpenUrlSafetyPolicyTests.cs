using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class OpenUrlSafetyPolicyTests
{
    private readonly OpenUrlSafetyPolicy _policy = new();

    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("http://example.test/path")]
    [InlineData("http://localhost:8080/classroom")]
    [InlineData("https://192.168.1.20/content")]
    [InlineData("http://pc01.lan/activity")]
    public void Validate_WhenHttpOrHttpsAbsoluteUrl_ReturnsValid(string url)
    {
        OpenUrlValidationResult result = _policy.Validate(url);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Uri);
    }

    [Theory]
    [InlineData("relative/page")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("ftp://example.test/file")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("https://user:pass@example.test/path")]
    [InlineData("https://example.test/\r\nnext")]
    [InlineData("https://example.test/\u0001")]
    public void Validate_WhenUrlIsUnsafe_ReturnsInvalid(string url)
    {
        OpenUrlValidationResult result = _policy.Validate(url);

        Assert.False(result.IsValid);
        Assert.Equal(SessionCommandErrorCodes.InvalidUrl, result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenUrlExceedsLimit_ReturnsInvalid()
    {
        var url = "https://example.test/" + new string('a', OpenUrlSafetyPolicy.MaxUrlLength);

        OpenUrlValidationResult result = _policy.Validate(url);

        Assert.False(result.IsValid);
        Assert.Equal(SessionCommandErrorCodes.InvalidUrl, result.ErrorCode);
    }
}
