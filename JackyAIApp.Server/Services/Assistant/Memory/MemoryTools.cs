using System.Text.Json;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Assistant.Memory
{
    /// <summary>The note a memory tool's call names (<c>text</c>), as it would be kept; null for none.</summary>
    internal static class MemoryInput
    {
        public static string? Of(JsonElement input)
        {
            var text = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                ? AssistantMemoryService.Normalize(t.GetString())
                : "";
            return text.Length > 0 ? text : null;
        }

        public static JsonElement Schema(string description) => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { text = new { type = "string", maxLength = AssistantMemoryService.MaxLength, description } },
            required = new[] { "text" },
        });

        /// <summary>Where the reader sees and forgets what Studio AI remembers.</summary>
        public static object Card(string description) =>
            new { kind = "link", title = "Studio AI 記得的事", href = "/connectors", description, actionLabel = "查看與管理" };
    }

    /// <summary>
    /// Keeps a note about the reader that Studio AI reads in every later conversation (docs/ai-native/design/08). A write,
    /// so the reader is asked first, and the card shows the note in full: a note is in every later prompt, so one slipped in
    /// by someone else's words would last.
    /// </summary>
    public sealed class RememberTool(IAssistantMemoryService memories) : ISiteTool, IDescribesCall
    {
        public const string Name = "remember";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "記住",
            "把一則關於使用者的事實或偏好記下來（例如「在準備多益，目標 800 分」「短文用商務情境」），之後的每個對話都會看到。" +
            "只在使用者要你記住、或說了「以後都…」時使用；用他的話寫成一句簡短的第三人稱筆記，一次一則；執行前會請使用者確認。",
            MemoryInput.Schema("要記住的一句話（200 字以內）"),
            ToolEffect.Write,
            // A note is in every later prompt, so it is always put to the reader, and never kept by another AI (D-101).
            AlwaysAsks: true);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context) =>
            MemoryInput.Of(input) is { } text ? $"記住：「{text}」" : null;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (MemoryInput.Of(input) is not { } text) return AiToolResult.Error("text 必須是要記住的一句話。");

            var kept = await memories.AddAsync(context.UserId, text, ct);
            var note = kept.Memory?.Text;
            return kept.Outcome switch
            {
                MemoryAddOutcome.Added => new AiToolResult(
                    $"已記住：「{note}」（共 {kept.Count} 則）。",
                    Card: MemoryInput.Card($"已記住：「{note}」・共 {kept.Count} 則")),
                MemoryAddOutcome.AlreadyKept => new AiToolResult(
                    $"本來就記得：「{note}」，沒有重複記。",
                    Card: MemoryInput.Card($"本來就記得：「{note}」")),
                MemoryAddOutcome.TooLong => AiToolResult.Error($"太長了，請濃縮成 {AssistantMemoryService.MaxLength} 字以內的一句話再記。"),
                MemoryAddOutcome.Full => AiToolResult.Error(
                    $"已經記了 {AssistantMemoryService.Limit} 則，滿了。請使用者先到 Connectors 的「Studio AI 記得的事」刪掉用不到的，或叫你忘掉某一則。"),
                _ => AiToolResult.Error("text 必須是要記住的一句話。"),
            };
        }
    }

    /// <summary>
    /// Forgets one of the reader's notes, named by its words as kept (so the card shows exactly what goes). It cannot be
    /// had back, so it always asks.
    /// </summary>
    public sealed class ForgetTool(IAssistantMemoryService memories) : ISiteTool, IDescribesCall
    {
        public const string Name = "forget";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "忘掉",
            "刪掉一則你記得的關於使用者的筆記。text 要一字不差地照抄「你記得的事」清單裡的那一則。只在使用者要你忘掉時使用；執行前會請使用者確認。",
            MemoryInput.Schema("要忘掉的那一則，照抄清單裡的文字"),
            ToolEffect.Destructive);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context) =>
            MemoryInput.Of(input) is { } text ? $"忘掉：「{text}」" : null;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (MemoryInput.Of(input) is not { } text) return AiToolResult.Error("text 必須是要忘掉的那一則。");

            var forgotten = await memories.RemoveTextAsync(context.UserId, text, ct);
            return forgotten == null
                ? AiToolResult.Error("找不到一字不差的這一則，請照抄你記得的事清單裡的文字。")
                : new AiToolResult($"已忘掉：「{forgotten.Text}」。", Card: MemoryInput.Card($"已忘掉：「{forgotten.Text}」"));
        }
    }
}
