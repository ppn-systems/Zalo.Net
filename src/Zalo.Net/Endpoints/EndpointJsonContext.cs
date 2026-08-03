using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Zalo.Net.Endpoints;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class EndpointJsonContext : JsonSerializerContext
{
}
