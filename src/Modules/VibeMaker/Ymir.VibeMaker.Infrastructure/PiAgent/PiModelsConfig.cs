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
    public static string Build(PiAgentOptions options, IEnumerable<ModelDescriptor> models)
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
                    ["models"] = new JsonArray(models.Select(ToModelNode).ToArray()),
                },
            },
        };
        return config.ToJsonString(s_indented);
    }

    private static JsonNode ToModelNode(ModelDescriptor model)
    {
        var node = new JsonObject { ["id"] = model.Id };
        if (model.SupportsThinking)
        {
            node["reasoning"] = true;
            node["compat"] = new JsonObject { ["supportsReasoningEffort"] = true };
        }
        if (model.SupportsImages)
        {
            node["input"] = new JsonArray("text", "image");
        }

        return node;
    }
}
