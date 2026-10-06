using Microsoft.Extensions.Logging.Abstractions;
using Ymir.RuntimeHost;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.UnitTests.Runtime;

/// <summary>Cloudflare Tunnel 管理（ADR-0010）：token 格式、unit 名稱、Disabled 模式。</summary>
public class TunnelManagementTests
{
    private static readonly string ValidToken = "eyJhIjoi" + new string('x', 120) + "+/_-.=";

    [Fact]
    public void TunnelToken_AcceptsBase64LikeTokens()
    {
        Assert.True(RuntimeHostProtocol.IsValidTunnelToken(ValidToken));
        Assert.True(RuntimeHostProtocol.IsValidTunnelToken(new string('A', RuntimeHostProtocol.MaximumTunnelTokenLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-token")]
    public void TunnelToken_RejectsMissingOrShortTokens(string? token) => Assert.False(RuntimeHostProtocol.IsValidTunnelToken(token));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\nOTHER=1")]
    [InlineData(" ")]
    [InlineData("\"")]
    [InlineData("$(id)")]
    [InlineData("中")]
    public void TunnelToken_RejectsCharactersThatCouldBreakTheEnvFile(string suffix) =>
        Assert.False(RuntimeHostProtocol.IsValidTunnelToken(ValidToken + suffix));

    [Fact]
    public void TunnelToken_RejectsTooLongTokens() =>
        Assert.False(RuntimeHostProtocol.IsValidTunnelToken(new string('A', RuntimeHostProtocol.MaximumTunnelTokenLength + 1)));

    [Theory]
    [InlineData("ymir-cloudflared.service", true)]
    [InlineData("cloudflared@ymir.service", true)]
    [InlineData("ymir-cloudflared", false)]
    [InlineData("--user.service", true)]
    [InlineData("a b.service", false)]
    [InlineData("x.service\n", false)]
    [InlineData("../evil.service", false)]
    public void UnitName_IsValidated(string unit, bool valid) =>
        Assert.Equal(valid, SystemdTunnelServiceController.IsValidUnit(unit));

    [Fact]
    public async Task DisabledMode_RefusesTokens_AndReportsNotManaged()
    {
        var controller = new RecordingController();
        var manager = new TunnelManager(new TunnelOptions { Mode = TunnelManagementMode.Disabled }, controller, NullLogger<TunnelManager>.Instance);

        Assert.Equal(TunnelSetResult.Disabled, await manager.SetTokenAsync(ValidToken, TestContext.Current.CancellationToken));
        Assert.False((await manager.GetStatusAsync(TestContext.Current.CancellationToken)).ManagementEnabled);
        Assert.Equal(0, controller.Restarts);
    }

    [Fact]
    public async Task EnvFile_IsReplacedAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ymir-tunnel-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "cloudflared.env");
        try
        {
            await TunnelManager.WriteEnvFileAsync(path, "first", TestContext.Current.CancellationToken);
            await TunnelManager.WriteEnvFileAsync(path, "second", TestContext.Current.CancellationToken);

            Assert.Equal("TUNNEL_TOKEN=second\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(directory));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingController : ITunnelServiceController
    {
        public int Restarts { get; private set; }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            Restarts++;
            return Task.CompletedTask;
        }

        public Task<bool> IsActiveAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
