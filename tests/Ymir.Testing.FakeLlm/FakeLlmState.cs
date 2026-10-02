using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Ymir.Testing.FakeLlm;

/// <summary>記錄收到的請求，讓測試可以驗證（例如 session 續接後訊息數有增加）。</summary>
public sealed class FakeLlmState
{
    private readonly ConcurrentQueue<JsonObject> _requests = new();

    public IReadOnlyCollection<JsonObject> Requests => _requests;

    public JsonObject? LastRequest => _requests.LastOrDefault();

    internal void Record(JsonObject request) => _requests.Enqueue(request);
}
