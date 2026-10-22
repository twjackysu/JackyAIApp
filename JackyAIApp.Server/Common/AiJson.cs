using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// JSON exchanged with the AI models: the examples and payloads written into prompts, and the
    /// answers parsed back from them. Both were written against Newtonsoft.Json, so where
    /// System.Text.Json's defaults are stricter these options keep what Newtonsoft accepted. A model
    /// answer it would have parsed must still parse; anything else is no looser than it was.
    /// Property names are written as the C# names (PascalCase), as before. The one thing Newtonsoft
    /// read that this cannot is JSON that isn't JSON (single quotes, bare names); the completions
    /// are requested in JSON mode, so a model never returns that.
    /// </summary>
    public static class AiJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            // A model writes "partOfSpeech" for PartOfSpeech as readily as the prompt's spelling.
            PropertyNameCaseInsensitive = true,
            // "Score": "8"
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            // Chinese as itself, not \uXXXX: six characters per character in every prompt. Nothing
            // written with these options ends up in HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                // "Aggregation": "Sum" (a number is still read too)
                new JsonStringEnumConverter(),
                new LenientStringConverter(),
                new LenientBoolConverter(),
                new LenientDateTimeConverter(),
                new LenientDecimalConverter(),
                new LenientDoubleConverter(),
            },
            // `required` is for our code building these types. A field the model leaves out comes
            // back null, as it did; the caller's validation decides whether that is acceptable.
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        foreach (var property in typeInfo.Properties)
                            property.IsRequired = false;
                    },
                },
            },
        };

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

        /// <summary>A number or true/false where a string belongs (a filter value of 2024, an option
        /// written as 1990) is read as its text.</summary>
        private sealed class LenientStringConverter : JsonConverter<string>
        {
            public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString(),
                    JsonTokenType.Number => Encoding.UTF8.GetString(
                        reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
                    JsonTokenType.True => "true",
                    JsonTokenType.False => "false",
                    _ => throw new JsonException($"Cannot read {reader.TokenType} as a string."),
                };

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value);
        }

        /// <summary>"true"/"false" in quotes, or 0/1, where a bool belongs.</summary>
        private sealed class LenientBoolConverter : JsonConverter<bool>
        {
            public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.True: return true;
                    case JsonTokenType.False: return false;
                    case JsonTokenType.Number when reader.TryGetInt64(out var number): return number != 0;
                    case JsonTokenType.String when bool.TryParse(reader.GetString(), out var parsed): return parsed;
                    default: throw new JsonException($"Cannot read {reader.TokenType} as a bool.");
                }
            }

            public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
                writer.WriteBooleanValue(value);
        }

        /// <summary>A price in quotes with a thousands separator ("1,050.5"). Newtonsoft read it;
        /// <see cref="JsonNumberHandling.AllowReadingFromString"/> alone does not.</summary>
        private sealed class LenientDecimalConverter : JsonConverter<decimal>
        {
            public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Number) return reader.GetDecimal();
                if (reader.TokenType == JsonTokenType.String &&
                    decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                throw new JsonException($"Cannot read {reader.TokenType} as a number.");
            }

            public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(value);
        }

        /// <inheritdoc cref="LenientDecimalConverter"/>
        private sealed class LenientDoubleConverter : JsonConverter<double>
        {
            public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble();
                if (reader.TokenType == JsonTokenType.String &&
                    double.TryParse(reader.GetString(), NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                throw new JsonException($"Cannot read {reader.TokenType} as a number.");
            }

            public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(value);
        }

        /// <summary>A date the way a model writes one ("2026-10-22 10:00:00"), not only ISO 8601.</summary>
        private sealed class LenientDateTimeConverter : JsonConverter<DateTime>
        {
            public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    if (reader.TryGetDateTime(out var iso)) return iso;
                    if (DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var parsed))
                        return parsed;
                }
                throw new JsonException($"Cannot read {reader.TokenType} as a date.");
            }

            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value);
        }
    }
}
