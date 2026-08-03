using System.Text.Json.Serialization;

namespace Zalo.Net.Contracts;

/// <summary>
/// Source-generated System.Text.Json serialization context for Native AOT and trimming support.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ZaloSessionMaterial))]
[JsonSerializable(typeof(ZaloQrSession))]
[JsonSerializable(typeof(ZaloLoginState))]
[JsonSerializable(typeof(ZaloSession))]
[JsonSerializable(typeof(ZaloAttachment))]
[JsonSerializable(typeof(ZaloMessageEvent))]
[JsonSerializable(typeof(ZaloSessionStatusChanged))]
[JsonSerializable(typeof(ZaloSendResult))]
[JsonSerializable(typeof(ZaloUserProfile))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(object))]
public partial class ZaloJsonContext : JsonSerializerContext
{
}
