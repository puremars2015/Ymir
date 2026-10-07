using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.IntegrationTests.RuntimeHost;

/// <summary>ADR-0008：API（RemoteRuntimeManager）經 Unix socket 呼叫 runtime host，stdio、exit code、取消與授權都要和本機 runtime 一致。</summary>
public class RuntimeHostTests(RuntimeHostFixture fixture) : IClassFixture<RuntimeHostFixture>
{
    private static async Task<(int ExitCode, string Output, IRuntimeProcess Process)> RunAsync(
        RemoteRuntimeManager manager,
        Guid runtimeId,
        RuntimeProcessSpec spec,
        string? stdin = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var process = await manager.StartProcessAsync(runtimeId, spec, ct);
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(Encoding.UTF8.GetBytes(stdin), ct);
        }

        process.CloseStandardInput();
        using var reader = new StreamReader(process.StandardOutput);
        var output = await reader.ReadToEndAsync(ct);
        var exitCode = await process.WaitForExitAsync(ct);
        return (exitCode, output, process);
    }

    [Fact]
    public async Task EnsureRuntime_CreatesUserDirectoriesOnTheHost()
    {
        var userId = Guid.NewGuid();

        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(userId, TestContext.Current.CancellationToken);
        var again = await fixture.RuntimeManager.EnsureRuntimeAsync(userId, TestContext.Current.CancellationToken);
        var status = await fixture.RuntimeManager.GetStatusAsync(runtime.RuntimeId, TestContext.Current.CancellationToken);

        Assert.Null(await fixture.RuntimeManager.CheckAvailabilityAsync(TestContext.Current.CancellationToken)); // 健康檢查
        // 建立 / 已在執行的資訊經協定傳回 API，供稽核使用（SA §12）
        Assert.Equal(RuntimeTransition.Created, runtime.Transition);
        Assert.Equal(RuntimeTransition.None, again.Transition);

        Assert.Equal(userId, runtime.UserId);
        Assert.Equal("LOCAL", runtime.Provider);
        Assert.Equal(runtime.RuntimeId, status.RuntimeId);
        Assert.True(Directory.Exists(UserDirectories.For(fixture.WorkspaceRoot, userId).Workspace));
    }

    [Fact]
    public async Task StatusAndStop_WorkByUserId_EvenFromAFreshApiInstance()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await fixture.RuntimeManager.EnsureRuntimeAsync(userId, ct);

        // API 重新啟動後的 manager 不認得任何 runtime id（ADR-0011）：查詢與停止只用 user id
        using var restarted = fixture.CreateManager(RuntimeHostFixture.Token);
        Assert.Equal(VibeMaker.Domain.RuntimeStatus.Running, await restarted.GetStatusForUserAsync(userId, ct));
        Assert.True(await restarted.StopForUserAsync(userId, ct));
        Assert.Equal(VibeMaker.Domain.RuntimeStatus.NotCreated, await restarted.GetStatusForUserAsync(userId, ct));

        var unknown = Guid.NewGuid();
        Assert.Equal(VibeMaker.Domain.RuntimeStatus.NotCreated, await restarted.GetStatusForUserAsync(unknown, ct));
        Assert.False(await restarted.StopForUserAsync(unknown, ct));
    }

    [Fact]
    public async Task Process_StreamsStdinToStdout_AndReturnsExitCode()
    {
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        var payload = string.Concat(Enumerable.Repeat("中文 stdin 0123456789\n", 5000)); // 跨越多個 WebSocket frame

        var (exitCode, output, process) = await RunAsync(fixture.RuntimeManager, runtime.RuntimeId, new RuntimeProcessSpec("sh", ["-c", "cat; exit 3"]), payload);
        await process.DisposeAsync();

        Assert.Equal(3, exitCode);
        Assert.Equal(payload, output);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Process_ReceivesEnvironmentAndWorkingDirectory_AndReportsStderr()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(userId, TestContext.Current.CancellationToken);
        var spec = new RuntimeProcessSpec(
            "sh",
            ["-c", "printf '%s' \"$YMIR_TEST_VALUE\" > env.txt; echo diagnostic >&2; exit 1"],
            new Dictionary<string, string> { ["YMIR_TEST_VALUE"] = "secret-value" },
            RuntimePaths.ProjectDirectory(projectId));

        var (exitCode, _, process) = await RunAsync(fixture.RuntimeManager, runtime.RuntimeId, spec);
        await process.DisposeAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains("diagnostic", process.GetStandardErrorTail(), StringComparison.Ordinal);
        var projectDirectory = UserDirectories.For(fixture.WorkspaceRoot, userId).HostPathOf(RuntimePaths.ProjectDirectory(projectId));
        Assert.Equal("secret-value", await File.ReadAllTextAsync(Path.Combine(projectDirectory, "env.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Kill_StopsTheProcess()
    {
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        var process = await fixture.RuntimeManager.StartProcessAsync(runtime.RuntimeId, new RuntimeProcessSpec("sleep", ["60"]), TestContext.Current.CancellationToken);

        process.Kill();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        await process.DisposeAsync();

        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task ClientDisconnect_TerminatesTheProcessOnTheHost()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await fixture.RuntimeManager.EnsureRuntimeAsync(userId, ct);
        using var connection = fixture.CreateConnection();
        using var socket = await connection.ConnectWebSocketAsync(RuntimeHostProtocol.ProcessPath(userId), ct);
        await SendStartAsync(socket, new ProcessStartMessage("sh", ["-c", "echo $$; exec sleep 60"], null, RuntimePaths.Workspace), ct);
        Assert.Equal(ProcessControlMessage.Started, (await ReceiveControlAsync(socket, ct))!.Type);
        var pid = int.Parse(Encoding.UTF8.GetString(await ReceiveBinaryAsync(socket, ct)).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Directory.Exists($"/proc/{pid}"));

        socket.Abort(); // API 異常結束：沒有 close handshake

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (Directory.Exists($"/proc/{pid}") && !IsZombie(pid) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, ct);
        }

        Assert.True(!Directory.Exists($"/proc/{pid}") || IsZombie(pid), "process should be terminated after the client disconnects");
    }

    [Fact]
    public async Task InvalidSpec_IsRejectedByTheHost_EvenWithoutClientValidation()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await fixture.RuntimeManager.EnsureRuntimeAsync(userId, ct);
        using var connection = fixture.CreateConnection();
        using var socket = await connection.ConnectWebSocketAsync(RuntimeHostProtocol.ProcessPath(userId), ct);

        await SendStartAsync(socket, new ProcessStartMessage("sh", ["-c", "id"], null, "/etc"), ct);
        var reply = await ReceiveControlAsync(socket, ct);

        Assert.Equal(ProcessControlMessage.Error, reply!.Type);
        Assert.DoesNotContain("/etc", reply.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSpec_IsRejectedByTheClient()
    {
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.RuntimeManager.StartProcessAsync(runtime.RuntimeId, new RuntimeProcessSpec("--privileged", []), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token-wrong-token-wrong-token-0000")]
    public async Task Requests_WithoutTheRightToken_AreRejected(string? token)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = fixture.CreateConnection(token ?? string.Empty);
        if (token is null)
        {
            connection.Http.DefaultRequestHeaders.Authorization = null;
        }

        using var ensure = await connection.Http.PostAsync(new Uri(RuntimeHostProtocol.RuntimePath(Guid.NewGuid()), UriKind.Relative), null, ct);
        var websocket = await Assert.ThrowsAsync<WebSocketException>(() => connection.ConnectWebSocketAsync(RuntimeHostProtocol.ProcessPath(Guid.NewGuid()), ct));

        Assert.Equal(HttpStatusCode.Unauthorized, ensure.StatusCode);
        Assert.NotNull(websocket);
    }

    [Fact]
    public async Task Health_IsAvailableWithoutToken_AndRevealsNothing()
    {
        using var connection = fixture.CreateConnection(string.Empty);
        connection.Http.DefaultRequestHeaders.Authorization = null;

        var body = await connection.Http.GetStringAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("ok", body);
    }

    [Fact]
    public async Task EmptyUserId_IsRejected()
    {
        using var connection = fixture.CreateConnection();

        using var response = await connection.Http.PostAsync(new Uri(RuntimeHostProtocol.RuntimePath(Guid.Empty), UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Manager_RefusesWeakToken()
    {
        Assert.Throws<InvalidOperationException>(() => fixture.CreateManager("short"));
    }

    [Fact]
    public async Task PiHarness_ThroughRuntimeHost_CreatesFileAndStreamsEvents()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(userId, ct);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in fixture.CreateHarness().RunAsync(
            PiHarnessFixture.Request(runtime.RuntimeId, Guid.NewGuid(), $"{FakeLlmScript.CreateFileMarker} 請建立檔案", RuntimePaths.ProjectDirectory(projectId), systemPrompts: ["你是測試助理"]),
            ct))
        {
            events.Add(agentEvent);
        }

        Assert.IsType<AgentStarted>(events[0]);
        Assert.Contains(events, e => e is AgentToolCompleted { Success: true });
        Assert.IsType<AgentCompleted>(events[^1]);
        var directory = UserDirectories.For(fixture.WorkspaceRoot, userId).HostPathOf(RuntimePaths.ProjectDirectory(projectId));
        Assert.Equal(FakeLlmScript.CreatedFileContent, await File.ReadAllTextAsync(Path.Combine(directory, FakeLlmScript.CreatedFileName), ct));
    }

    [Fact]
    public async Task PiHarness_ThroughRuntimeHost_CancellationEndsWithCancelled()
    {
        Assert.SkipUnless(PiHarnessFixture.IsPiOnPath(), "pi is not on PATH (npm i -g @earendil-works/pi-coding-agent@1.0.0)");
        var runtime = await fixture.RuntimeManager.EnsureRuntimeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in fixture.CreateHarness().RunAsync(
            PiHarnessFixture.Request(runtime.RuntimeId, Guid.NewGuid(), $"{FakeLlmScript.SlowMarker} 慢慢講", RuntimePaths.Workspace),
            cts.Token))
        {
            events.Add(agentEvent);
            if (agentEvent is AgentTextDelta)
            {
                await cts.CancelAsync();
            }
        }

        Assert.IsType<AgentCancelled>(events[^1]);
        Assert.DoesNotContain(events, e => e is AgentFailed { Code: ExecutionErrorCodes.AgentRuntimeError });
    }

    private static bool IsZombie(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].StartsWith('Z');
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static Task SendStartAsync(WebSocket socket, ProcessStartMessage message, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, RuntimeHostProtocol.JsonOptions), WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<ProcessControlMessage?> ReceiveControlAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonSerializer.Deserialize<ProcessControlMessage>(buffer.AsSpan(0, result.Count), RuntimeHostProtocol.JsonOptions);
    }

    private static async Task<byte[]> ReceiveBinaryAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        return buffer[..result.Count];
    }
}
