using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BosunReporter.Infrastructure
{
    class JsonEpochConverter : JsonConverter<DateTime>
    {
        static readonly DateTime s_epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return s_epoch.AddSeconds(reader.GetInt64());
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue((long)(value - s_epoch).TotalSeconds);
        }
    }
}
