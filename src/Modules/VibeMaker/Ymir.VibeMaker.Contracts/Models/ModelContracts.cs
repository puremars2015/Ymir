namespace Ymir.VibeMaker.Contracts.Models;

/// <summary><c>GET /api/models</c>：可在對話中選用的模型（Id 對應 LiteLLM 的 model_name）。</summary>
/// <param name="SupportsImages">可直接看使用者附加的圖片。</param>
/// <param name="AllowKnowledgeBase">可用於知識庫問答（ADR-0014 §8）。</param>
public sealed record ModelResponse(string Id, string DisplayName, bool IsDefault, bool SupportsImages, bool AllowKnowledgeBase = false);
