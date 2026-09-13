namespace JackyAIApp.Server.Services.Dictionary
{
    /// <summary>
    /// Single source of truth for a word's public-facing SEO title/description text.
    /// <see cref="Mappers.WordMapper.ToDto"/> calls this to populate <see cref="DTO.Word.SeoTitle"/>/
    /// <see cref="DTO.Word.SeoDescription"/> on the <c>GET api/dictionary/{word}</c> response, which the
    /// frontend (TASK-002) renders verbatim and the bot-preview middleware (TASK-003) reads via that same
    /// response in-process. Do not reformat this text a second time anywhere else — see
    /// .agent/architecture/ARCHITECTURE.md ("Single-source title/description text") for why.
    /// </summary>
    public static class WordSeoTextFormatter
    {
        private const string TitleSuffix = "EngAI Learn Dictionary";
        private const int DescriptionMaxLength = 160;

        public static string FormatTitle(string wordText) => $"{wordText} - {TitleSuffix}";

        public static string FormatDescription(string wordText, IReadOnlyList<DTO.WordMeaning> meanings)
        {
            var firstMeaning = meanings.FirstOrDefault(m => m.Definitions.Count > 0);
            var firstDefinition = firstMeaning?.Definitions.FirstOrDefault();
            if (firstMeaning == null || firstDefinition == null || string.IsNullOrWhiteSpace(firstDefinition.English))
            {
                return string.Empty;
            }

            var prefix = $"{wordText} ({firstMeaning.PartOfSpeech}): ";
            var remaining = DescriptionMaxLength - prefix.Length;
            if (remaining <= 0)
            {
                return Truncate(prefix, DescriptionMaxLength);
            }

            return prefix + Truncate(firstDefinition.English.Trim(), remaining);
        }

        private static string Truncate(string text, int maxLength)
        {
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text[..(maxLength - 1)].TrimEnd() + "…";
        }
    }
}
