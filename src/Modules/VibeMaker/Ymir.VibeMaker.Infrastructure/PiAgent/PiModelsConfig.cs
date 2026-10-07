using System.Text.Json;
using System.Text.Json.Nodes;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>產生 Pi 的 models.json：單一 OpenAI 相容 provider 指向 LiteLLM，列出所有可選用的模型；API key 以環境變數注入（不寫入檔案）。</summary>
internal static class PiModelsConfig
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    public static string Build(PiAgentOptions options, IEnumerable<string> modelIds) =>
        Build(options, modelIds.Select(id => new ModelDescriptor(id, id)));

    /// <summary>支援視覺的模型宣告 <c>input: ["text", "image"]</c>：Pi 才會把附加的圖片（與 read 工具讀到的圖片）送給模型，並自動縮圖。</summary>
    public static string Build(PiAgentOptions options, IEnumerable<ModelDescriptor> models, string? disabledThinkingModelId = null, string? defaultThinkingModelId = null)
    {
        var config = new JsonObject
        {
            ["providers"] = new JsonObject
            {
                [options.ProviderName] = new JsonObject
                {
                    ["baseUrl"] = options.ModelBaseUrl.ToString().TrimEnd('/'),
                    ["api"] = "openai-completions",
                    ["apiKey"] = "${" + PiRuntimeLayout.ApiKeyEnvironmentVariable + "}",
                    ["models"] = new JsonArray(models.Select(model => ToModelNode(model, disabledThinkingModelId, defaultThinkingModelId)).ToArray()),
                },
            },
        };
        return config.ToJsonString(s_indented);
    }

    private static JsonObject ToModelNode(ModelDescriptor model, string? disabledThinkingModelId, string? defaultThinkingModelId)
    {
        var node = new JsonObject { ["id"] = model.Id };
        if (model.EffectiveThinking is { } thinking)
        {
            node["reasoning"] = model.Id != defaultThinkingModelId;
            node["compat"] = new JsonObject
            {
                ["supportsReasoningEffort"] = true,
                ["thinkingFormat"] = thinking.Parameter == "reasoning.effort" ? "openrouter" : "openai",
            };
            // Pi 的 off 與「模型預設」分開：只有明確 none 的這次執行送出 none。
            var levelMap = new JsonObject();
            if (model.Id == disabledThinkingModelId) levelMap["off"] = "none";
            foreach (var level in thinking.Levels.Where(level => level != "none")) levelMap[level] = level;
            node["thinkingLevelMap"] = levelMap;
        }
        if (model.SupportsImages)
        {
            node["input"] = new JsonArray("text", "image");
        }

        return node;
    }
}
