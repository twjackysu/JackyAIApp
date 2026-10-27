using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Reads a session upload's rows as the default reader would (an array of objects of strings; a null value
    /// kept, a repeated name's last value kept), but stops once their estimated in-memory size — counted as
    /// <see cref="SessionDataStore.Measure"/> counts it — passes <see cref="SessionUploadOptions.MaxReadBytes"/>.
    /// The upload is anonymous and its body is only checked by length before it is read, while tiny cells cost
    /// far more in memory than in JSON (<c>{}</c> is 3 bytes and a row 136): 32 MB of empty rows would otherwise
    /// be about a gigabyte before the store measured anything (D-158). Past the limit it is a
    /// <see cref="JsonException"/>, which the API answers as a 400 like any body it cannot read.
    /// </summary>
    public sealed class SessionRowsJsonConverter : JsonConverter<List<Dictionary<string, string>>>
    {
        public override List<Dictionary<string, string>> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Rows must be an array.");

            var rows = new List<Dictionary<string, string>>();
            long bytes = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return rows;
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("Each row must be an object.");

                bytes += SessionDataStore.PerRowOverheadBytes;
                var row = new Dictionary<string, string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var key = reader.GetString()!;
                    reader.Read();
                    var value = reader.TokenType switch
                    {
                        JsonTokenType.String => reader.GetString(),
                        JsonTokenType.Null => null,
                        _ => throw new JsonException("Every cell must be a string."),
                    };
                    row[key] = value!;
                    bytes += SessionDataStore.PerCellOverheadBytes + 2L * (key.Length + (value?.Length ?? 0));
                }
                if (bytes > SessionUploadOptions.MaxReadBytes)
                    throw new JsonException("The rows are too large.");
                rows.Add(row);
            }
            throw new JsonException("Rows ended early.");
        }

        public override void Write(
            Utf8JsonWriter writer, List<Dictionary<string, string>> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }
}
