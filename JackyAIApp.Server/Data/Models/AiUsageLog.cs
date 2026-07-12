using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Append-only record of every billable AI API call (chat completion, TTS, Whisper).
    /// Written fire-and-forget from the request path — logging failures never affect the user.
    /// Intended to be queried by the DataAnalysis dashboard (point a MsSql data source at this
    /// same database) to see which feature drives token/character spend.
    /// </summary>
    public class AiUsageLog
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// Feature that made the call, dotted for easy grouping — e.g. "exam.cloze",
        /// "dictionary.wordbase", "finance.stock_analysis", "finance.stockcode_resolver",
        /// "dataanalysis.nl_summarize", "tts", "whisper".
        /// </summary>
        [Required, MaxLength(100)]
        public required string Feature { get; set; }

        /// <summary>Coarse call type: "chat" | "tts" | "whisper" | "image".</summary>
        [Required, MaxLength(20)]
        public required string Operation { get; set; }

        [Required, MaxLength(60)]
        public required string Model { get; set; }

        /// <summary>Chat only — input tokens billed. Null for TTS/Whisper.</summary>
        public int? PromptTokens { get; set; }

        /// <summary>Chat only — output tokens billed (includes reasoning tokens on reasoning models).</summary>
        public int? CompletionTokens { get; set; }

        /// <summary>Chat only — prompt + completion.</summary>
        public int? TotalTokens { get; set; }

        /// <summary>TTS only — input character count (TTS is billed per character).</summary>
        public int? CharacterCount { get; set; }

        /// <summary>Image generation only — number of images produced (billed per image).</summary>
        public int? ImageCount { get; set; }

        /// <summary>
        /// Best-effort USD cost from <see cref="Common.AiRates"/> at log time — approximate, for
        /// dashboard SUMs. Raw token/char columns above are the authoritative source if rates drift.
        /// Null when the rate for a model isn't known (e.g. Whisper without a duration).
        /// </summary>
        // High precision: a single call can cost a tiny fraction of a cent; decimal(18,2) would
        // round every per-call cost to 0.00 and make dashboard SUMs meaningless.
        [Column(TypeName = "decimal(18,8)")]
        public decimal? EstimatedCostUsd { get; set; }

        /// <summary>Whether the underlying API call succeeded (failed calls can still bill tokens).</summary>
        public bool Success { get; set; }

        /// <summary>Owner if the call was made in an authenticated context; null for anonymous (e.g. public finance).</summary>
        [MaxLength(255)]
        public string? UserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
