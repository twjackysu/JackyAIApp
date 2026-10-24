using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Repository;

namespace JackyAIApp.Server.Services.Dictionary.Assistant
{
    /// <summary>The word a tool call names, if it is a plain English word (or a short phrase).</summary>
    internal static partial class WordInput
    {
        [GeneratedRegex(@"^[A-Za-z][A-Za-z' -]{0,49}$")]
        private static partial Regex Plain();

        internal const string NotAWordMessage = "word 必須是一個英文單字（或很短的片語），只能有英文字母、空格、連字號或撇號。";

        public static string? Of(JsonElement input)
        {
            var word = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("word", out var w) && w.ValueKind == JsonValueKind.String
                ? w.GetString()?.Trim()
                : null;
            return word != null && Plain().IsMatch(word) ? word.ToLowerInvariant() : null;
        }

        public static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { word = new { type = "string", description = "英文單字，例如 serendipity" } },
            required = new[] { "word" },
        });

        public static string DictionaryPath(string word) => $"/english/dictionary/{Uri.EscapeDataString(word)}";
    }

    /// <summary>Looks a word up in the site's dictionary (as its page does: one not stored yet is generated once and kept).</summary>
    public sealed class LookUpWordTool(IDictionaryService dictionary) : ISiteTool, IOffersAppActions
    {
        public const string Name = "look_up_word";

        /// <summary>The most meanings, and definitions of each, the model is sent.</summary>
        private const int MaxMeanings = 4;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "查字典",
            "在本站的英文字典查一個單字：KK 音標、詞性、中英文釋義、例句、同反義字。回答單字的意思前先查，不要憑記憶。",
            WordInput.Schema,
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        /// <summary>In another AI's frame, the word's card can add it to the repository with one press.</summary>
        public IReadOnlyList<SiteAppAction> AppActionsFor(JsonElement input) =>
            WordInput.Of(input) is { } word
                ? [new SiteAppAction(AddWordToRepositoryTool.Name, "加入單字庫", new JsonObject { ["word"] = word })]
                : [];

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (WordInput.Of(input) is not { } word) return AiToolResult.Error(WordInput.NotAWordMessage);
            var found = await dictionary.GetWordAsync(word, ct);
            if (!found.IsSuccess)
                return AiToolResult.Error(found.Error == ErrorCodes.NotFound || found.Error == ErrorCodes.BadRequest
                    ? $"字典裡找不到「{word}」，可能拼錯了。"
                    : "字典暫時查不到，請告訴使用者稍後再試。");

            var entry = found.Data;
            var text = new StringBuilder($"{entry.Word} {entry.KKPhonics}\n");
            foreach (var meaning in entry.Meanings.Take(MaxMeanings))
            {
                text.Append($"({meaning.PartOfSpeech}) ");
                text.AppendJoin("；", meaning.Definitions.Take(2).Select(d => $"{d.Chinese}（{d.English}）"));
                if (meaning.ExampleSentences.FirstOrDefault() is { } example)
                    text.Append($"\n  例：{example.English}｜{example.Chinese}");
                if (meaning.Synonyms.Count > 0) text.Append($"\n  同義：{string.Join(", ", meaning.Synonyms.Take(5))}");
                text.Append('\n');
            }
            return new AiToolResult(text.ToString().TrimEnd(), Card: new
            {
                kind = "link",
                title = $"{entry.Word} {entry.KKPhonics}",
                href = WordInput.DictionaryPath(word),
                description = entry.Meanings.FirstOrDefault()?.Definitions.FirstOrDefault()?.Chinese,
                actionLabel = "看完整解釋",
            });
        }
    }

    /// <summary>Adds a word to the reader's word repository — a write, so the reader is asked first.</summary>
    public sealed class AddWordToRepositoryTool(IDictionaryService dictionary, IRepositoryService repository) : ISiteTool, IDescribesCall
    {
        public const string Name = "add_word_to_repository";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "加入單字庫",
            "把一個英文單字加入使用者的單字庫（之後會出現在複習與測驗裡）。只在使用者要求時使用；執行前會請使用者確認。",
            WordInput.Schema,
            ToolEffect.Write);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context) =>
            WordInput.Of(input) is { } word ? $"把「{word}」加入你的單字庫" : null;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (WordInput.Of(input) is not { } word) return AiToolResult.Error(WordInput.NotAWordMessage);
            var found = await dictionary.GetWordAsync(word, ct);
            if (!found.IsSuccess) return AiToolResult.Error($"字典裡找不到「{word}」，所以沒有加入。");

            var added = await repository.AddWordAsync(found.Data.Id);
            var card = new { kind = "link", title = "我的單字庫", href = "/english/repository", description = $"「{found.Data.Word}」在你的單字庫裡", actionLabel = "前往單字庫" };
            if (added.IsSuccess) return new AiToolResult($"已把「{found.Data.Word}」加入使用者的單字庫。", Card: card);
            // The repository's one refusal for a word that exists: it is there already.
            return added.Error == ErrorCodes.BadRequest
                ? new AiToolResult($"「{found.Data.Word}」本來就在使用者的單字庫裡，沒有重複加入。", Card: card)
                : AiToolResult.Error("加入單字庫失敗，請告訴使用者稍後再試。");
        }
    }
    /// <summary>
    /// The words the reader saved most recently, each with its first meaning, and how many they have — so Studio AI
    /// can write with the reader's own words (a short passage, sentences) or talk about their collection. The words
    /// and meanings are the site's dictionary's, not anyone else's text.
    /// </summary>
    public sealed class SavedWordsTool(IRepositoryService repository) : ISiteTool
    {
        public const string Name = "saved_words";

        internal const int DefaultCount = 15;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "我的單字庫",
            "列出使用者單字庫裡最近加入的單字（新的在前，含詞性與中文意思）與單字庫的總字數。" +
            "使用者想用自己的單字寫短文、造句、出題以外的練習，或問單字庫裡有哪些字時使用。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    count = new
                    {
                        type = "integer",
                        minimum = 1,
                        maximum = SavedWords.MaxRecent,
                        description = $"要列出幾個最近加入的字，預設 {DefaultCount}",
                    },
                },
            }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        private static readonly object DictionaryCard = new
        {
            kind = "link",
            title = "字典",
            href = "/english/dictionary",
            description = "查單字並加入單字庫",
            actionLabel = "去查單字",
        };

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var count = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("count", out var given) &&
                        given.ValueKind == JsonValueKind.Number && given.TryGetInt32(out var n)
                ? n
                : DefaultCount;
            var read = await repository.GetRecentWordsAsync(count, ct);
            if (!read.IsSuccess) return AiToolResult.Error("單字庫暫時讀不到，請告訴使用者稍後再試。");

            var saved = read.Data!;
            if (saved.Words.Count == 0)
                return new AiToolResult(
                    "使用者的單字庫還是空的。建議他先查幾個字並加入單字庫（可以用 look_up_word、add_word_to_repository）。",
                    Card: DictionaryCard);

            var text = new StringBuilder();
            text.AppendLine($"使用者的單字庫共 {saved.Total} 個字，最近加入的 {saved.Words.Count} 個（新的在前）：");
            foreach (var w in saved.Words)
            {
                var meaning = string.Join(" ", new[] { w.PartOfSpeech is { Length: > 0 } pos ? $"({pos})" : null, w.Chinese }.Where(x => !string.IsNullOrWhiteSpace(x)));
                text.AppendLine(meaning.Length > 0 ? $"- {w.Word} {meaning}" : $"- {w.Word}");
            }
            text.AppendLine("用這些字寫短文或造句時，把用到的單字用粗體標出，並在最後附上中文翻譯。");

            return new AiToolResult(text.ToString().TrimEnd(), Card: new
            {
                kind = "link",
                title = "我的單字庫",
                href = "/english/repository",
                description = $"共 {saved.Total} 個字，最近加入：{string.Join("、", saved.Words.Take(5).Select(w => w.Word))}",
                actionLabel = "前往單字庫",
            });
        }
    }
}
