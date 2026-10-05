using System.Text.Json;
using System.Text.Json.Serialization;
using Zuil.Graph.Dto;

namespace Zuil.Graph;

/// <summary>
/// Source-generated serializer. Keeps Graph deserialization reflection-free, so
/// single-file publish (and later, trimming) stays safe.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CalendarViewResponse))]
internal sealed partial class GraphJsonContext : JsonSerializerContext
{
}
