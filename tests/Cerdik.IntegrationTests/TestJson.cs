using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cerdik.IntegrationTests;

/// <summary>JSON options matching the API: camelCase + string enums. The API serializes enums as
/// strings (JsonStringEnumConverter), so test clients must read them the same way.</summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
