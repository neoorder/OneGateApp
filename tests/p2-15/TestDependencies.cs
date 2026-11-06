using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Services
{
    // Only serializer behavior needed by the RPC boundary; no MAUI filesystem.
    static class SharedOptions
    {
        public static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new StrictIntegerConverter() }
        };
    }

    sealed class StrictIntegerConverter : JsonConverter<StrictInteger>
    {
        public override StrictInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(int.Parse(reader.GetString()!, CultureInfo.InvariantCulture));

        public override void Write(Utf8JsonWriter writer, StrictInteger value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Value);
    }

    sealed record StrictInteger(int Value);
}

namespace NeoOrder.OneGate.Models
{
    // Test data carrier for DapiException.Data; VM models are outside this harness.
    public sealed record InvocationResult(string State, string? Exception);
}
