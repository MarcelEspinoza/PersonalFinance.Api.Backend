using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalFinance.Api.Utils
{
    public class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s)) return default;
            // Parse and force to UTC
            var dt = DateTime.Parse(s, null, DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("o"));
        }
    }
}