using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Ymir.Testing.FakeLlm;

/// <summary>記錄收到的請求，讓測試可以驗證（例如 session 續接後訊息數有增加、Agent 用的是哪一把 key）。</summary>
public sealed class FakeLlmState
{
    private readonly ConcurrentQueue<JsonObject> _requests = new();
    private readonly ConcurrentQueue<string?> _usedApiKeys = new();
    private readonly ConcurrentQueue<JsonObject> _generateRequests = new();
    private readonly ConcurrentDictionary<string, string> _activeKeysByToken = new();

    /// <param name="masterKey">
    /// 設定時模擬 LiteLLM 的 key management（<c>/key/generate</c>、<c>/key/delete</c>），
    /// 且 <c>/v1/chat/completions</c> 只接受已發放、未撤銷的 virtual key（連 master key 都拒絕，用來抓出金鑰外洩）。
    /// </param>
    public FakeLlmState(string? masterKey = null) => MasterKey = masterKey;

    public string? MasterKey { get; }

    public IReadOnlyCollection<JsonObject> Requests => _requests;

    public JsonObject? LastRequest => _requests.LastOrDefault();

    /// <summary>每次 chat completions 帶的 Bearer token（沒帶為 null）。</summary>
    public IReadOnlyCollection<string?> UsedApiKeys => _usedApiKeys;

    public IReadOnlyCollection<JsonObject> GenerateRequests => _generateRequests;

    public IReadOnlyCollection<string> ActiveKeys => _activeKeysByToken.Values.ToList();

    internal void Record(JsonObject request, string? apiKey)
    {
        _requests.Enqueue(request);
        _usedApiKeys.Enqueue(apiKey);
    }

    internal (string Key, string Token) Issue(JsonObject request)
    {
        _generateRequests.Enqueue(request);
        var key = $"sk-fake-{Guid.NewGuid():N}";
        var token = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        _activeKeysByToken[token] = key;
        return (key, token);
    }

    /// <summary>接受 hashed token 或 key 本身（同 LiteLLM）。</summary>
    internal bool Revoke(string keyOrToken)
    {
        if (_activeKeysByToken.TryRemove(keyOrToken, out _))
        {
            return true;
        }

        var match = _activeKeysByToken.FirstOrDefault(kv => kv.Value == keyOrToken);
        return match.Key is not null && _activeKeysByToken.TryRemove(match.Key, out _);
    }

    internal bool IsActive(string? apiKey) => apiKey is not null && _activeKeysByToken.Values.Contains(apiKey);
}
