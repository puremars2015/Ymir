using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ymir.IntegrationTests.Api;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
