namespace Ymir.VibeMaker.Contracts.Models;

/// <summary><c>GET /api/models</c>：可在對話中選用的模型（Id 對應 LiteLLM 的 model_name）。</summary>
public sealed record ModelResponse(string Id, string DisplayName, bool IsDefault);
