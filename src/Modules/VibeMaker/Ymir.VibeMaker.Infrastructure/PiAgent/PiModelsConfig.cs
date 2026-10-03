using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>產生 Pi 的 models.json：單一 OpenAI 相容 provider 指向 LiteLLM，API key 以環境變數注入（不寫入檔案）。</summary>
internal static class PiModelsConfig
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    public static string Build(PiAgentOptions options)
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
                    ["models"] = new JsonArray(new JsonObject { ["id"] = options.ModelId }),
                },
            },
        };
        return config.ToJsonString(s_indented);
    }
}
