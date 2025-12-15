using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalFinance.Api.Api.Utils
{
    public class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrEmpty(s))
                return default;

            // 1) Intentamos parsear como "assume universal" (trata cadenas sin zona como UTC) y ajustamos a UTC
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            // 2) Si falla, intentamos formato round-trip ISO 8601 ("o")
            if (DateTime.TryParseExact(s, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
            {
                // Convertimos a UTC de forma segura y forzamos Kind=Utc
                return DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc);
            }

            // 3) Fallback: intentar parse sin estilos específicos (último recurso)
            dt = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Siempre escribir en formato ISO UTC
            writer.WriteStringValue(value.ToUniversalTime().ToString("o"));
        }
    }
}