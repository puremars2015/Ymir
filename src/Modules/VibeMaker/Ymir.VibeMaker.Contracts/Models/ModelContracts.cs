namespace Ymir.VibeMaker.Contracts.Models;

/// <summary><c>GET /api/models</c>：可在對話中選用的模型（Id 對應 LiteLLM 的 model_name）。</summary>
/// <param name="SupportsImages">可直接看使用者附加的圖片。</param>
public sealed record ModelResponse(string Id, string DisplayName, bool IsDefault, bool SupportsImages, bool SupportsThinking = false, ThinkingCapabilityResponse? Thinking = null, bool AllowKnowledgeBase = false);

public sealed record ThinkingCapabilityResponse(string Parameter, IReadOnlyList<string> Levels, string? DefaultLevel, bool Required);
