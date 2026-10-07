using Ymir.RuntimeHost;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.UnitTests.Runtime;

/// <summary>ADR-0008：runtime host 只接受 loopback / Unix socket、強 token，且程序規格不能影響 podman exec 的參數解析。</summary>
public class RuntimeHostProtocolTests
{
    private static RuntimeProcessSpec Spec(
        string executable = "pi",
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string workingDirectory = RuntimePaths.Workspace) =>
        new(executable, arguments ?? ["--mode", "rpc"], environment, workingDirectory);

    [Theory]
    [InlineData("unix:/run/ymir-runtime/runtime.sock", "/run/ymir-runtime/runtime.sock")]
    [InlineData("unix:/tmp/x.sock", "/tmp/x.sock")]
    public void Endpoint_UnixSocket_RequiresAbsolutePath(string value, string expectedPath)
    {
        var endpoint = RuntimeHostEndpoint.Parse(value, "setting");

        Assert.True(endpoint.IsUnixSocket);
        Assert.Equal(expectedPath, endpoint.SocketPath);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5090")]
    [InlineData("http://localhost:5090/")]
    [InlineData("http://[::1]:5090")]
    public void Endpoint_Http_AllowsOnlyLoopback(string value)
    {
        var endpoint = RuntimeHostEndpoint.Parse(value, "setting");

        Assert.False(endpoint.IsUnixSocket);
        Assert.NotNull(endpoint.HttpUri);
    }

    [Theory]
    [InlineData("http://host.docker.internal:5090")]
    [InlineData("http://host.containers.internal:5090/")]
    public void Endpoint_ContainerHostAlias_OnlyAllowedForTheClient(string value)
    {
        var endpoint = RuntimeHostEndpoint.Parse(value, "setting", allowContainerHostAlias: true);

        Assert.True(endpoint.UsesContainerHostAlias);
        // runtime host 監聽的位址不接受別名，只能是 loopback。
        Assert.Throws<InvalidOperationException>(() => RuntimeHostEndpoint.Parse(value, "setting"));
    }

    [Theory]
    [InlineData("https://host.docker.internal:5090")]
    [InlineData("http://host.docker.internal.evil.example:5090")]
    [InlineData("http://evil.example:5090")]
    public void Endpoint_ContainerHostAlias_IsExactAndHttpOnly(string value)
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeHostEndpoint.Parse(value, "setting", allowContainerHostAlias: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unix:relative.sock")]
    [InlineData("http://10.0.0.5:5090")]
    [InlineData("http://0.0.0.0:5090")]
    [InlineData("http://runtime.example.com:5090")]
    [InlineData("https://127.0.0.1:5090")]
    [InlineData("http://127.0.0.1:5090/prefix")]
    [InlineData("tcp://127.0.0.1:5090")]
    public void Endpoint_RejectsEverythingElse(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeHostEndpoint.Parse(value, "setting"));
    }

    [Fact]
    public void Token_MustBeLongEnough()
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeHostProtocol.EnsureTokenIsStrong(null, "setting"));
        Assert.Throws<InvalidOperationException>(() => RuntimeHostProtocol.EnsureTokenIsStrong("short", "setting"));
        RuntimeHostProtocol.EnsureTokenIsStrong(new string('x', RuntimeHostProtocol.MinimumTokenLength), "setting");
    }

    [Fact]
    public void TokenMatches_ComparesExactly()
    {
        var token = new string('a', 40);

        Assert.True(RuntimeHostProtocol.TokenMatches(token, token));
        Assert.False(RuntimeHostProtocol.TokenMatches(token, token + "b"));
        Assert.False(RuntimeHostProtocol.TokenMatches(token, token.ToUpperInvariant()));
        Assert.False(RuntimeHostProtocol.TokenMatches(token, null));
        Assert.False(RuntimeHostProtocol.TokenMatches(token, string.Empty));
    }

    [Fact]
    public void Validate_AcceptsHarnessSpecs()
    {
        var spec = Spec(
            environment: new Dictionary<string, string> { ["LITELLM_API_KEY"] = "sk-x", ["PI_OFFLINE"] = "1" },
            workingDirectory: RuntimePaths.ProjectDirectory(Guid.NewGuid()));

        Assert.Null(RuntimeHostProtocol.Validate(spec));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--privileged")]
    [InlineData("-v")]
    [InlineData("pi\0")]
    public void Validate_RejectsExecutablesThatLookLikeOptions(string executable)
    {
        Assert.NotNull(RuntimeHostProtocol.Validate(Spec(executable)));
    }

    [Theory]
    [InlineData("--privileged")]
    [InlineData("A=B")]
    [InlineData("1ABC")]
    [InlineData("")]
    [InlineData("WITH SPACE")]
    public void Validate_RejectsInvalidEnvironmentNames(string name)
    {
        Assert.NotNull(RuntimeHostProtocol.Validate(Spec(environment: new Dictionary<string, string> { [name] = "x" })));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/etc")]
    [InlineData("/workspace/../etc")]
    [InlineData("/workspace/projects/not-a-guid")]
    [InlineData("/agent-state")]
    public void Validate_RejectsWorkingDirectoriesOutsideTheAllowList(string workingDirectory)
    {
        Assert.NotNull(RuntimeHostProtocol.Validate(Spec(workingDirectory: workingDirectory)));
    }

    [Fact]
    public void Validate_RejectsNulInArgumentsAndTooManyArguments()
    {
        Assert.NotNull(RuntimeHostProtocol.Validate(Spec(arguments: ["ok", "bad\0"])));
        Assert.NotNull(RuntimeHostProtocol.Validate(Spec(arguments: Enumerable.Repeat("x", RuntimeHostProtocol.MaxArguments + 1).ToList())));
    }

    [Theory]
    [InlineData(null, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite)]
    [InlineData("600", UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData("0660", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite)]
    public void SocketMode_ParsesOctal(string? value, UnixFileMode expected)
    {
        Assert.Equal(expected, RuntimeHostApp.ParseSocketMode(value));
    }

    [Theory]
    [InlineData("666")]
    [InlineData("777")]
    [InlineData("664")]
    [InlineData("abc")]
    [InlineData("689")]
    [InlineData("-660")]
    public void SocketMode_RejectsOtherAccessAndGarbage(string value)
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeHostApp.ParseSocketMode(value));
    }

    [Fact]
    public void EnsurePath_SendsOnlyTheNetworkEnum()
    {
        var userId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        Assert.Equal("/v1/users/6f9619ff8b86d011b42d00c04fc964ff/runtime", RuntimeHostProtocol.EnsurePath(userId, null));
        Assert.Equal("/v1/users/6f9619ff8b86d011b42d00c04fc964ff/runtime?network=restricted", RuntimeHostProtocol.EnsurePath(userId, RuntimeNetworkAccess.Restricted));
        Assert.Equal("/v1/users/6f9619ff8b86d011b42d00c04fc964ff/runtime?network=internet", RuntimeHostProtocol.EnsurePath(userId, RuntimeNetworkAccess.Internet));
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("internet", true, RuntimeNetworkAccess.Internet)]
    [InlineData("restricted", true, RuntimeNetworkAccess.Restricted)]
    [InlineData("Restricted", false, null)]
    [InlineData("ymir-agents", false, null)]
    [InlineData("host", false, null)]
    [InlineData("1", false, null)]
    [InlineData("", false, null)]
    public void TryParseNetwork_AcceptsOnlyTheEnumValues(string? value, bool valid, RuntimeNetworkAccess? expected)
    {
        // ADR-0012 A.8 / ADR-0008：runtime host 不接受 network 名稱或其他資源設定。
        Assert.Equal(valid, RuntimeHostProtocol.TryParseNetwork(value, out var network));
        Assert.Equal(expected, network);
    }
}
