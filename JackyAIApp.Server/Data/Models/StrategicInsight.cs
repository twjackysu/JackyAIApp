using Newtonsoft.Json;

namespace JackyAIApp.Server.Data.Models
{
  /// <summary>
  /// Represents strategic insights extracted from Taiwan Stock Exchange's daily material information.
  /// </summary>
  public class StrategicInsight
  {
    /// <summary>
    /// Company stock code, e.g., "6838"
    /// </summary>
    [JsonProperty("stockCode")]
    public string StockCode { get; set; } = string.Empty;

    /// <summary>
    /// Company name, e.g., "台新藥"
    /// </summary>
    [JsonProperty("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Publication date, format: YYYY-MM-DD
    /// </summary>
    [JsonProperty("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Original title summary
    /// </summary>
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Why this event matters (condensed explanation)
    /// </summary>
    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Expected market reaction: 'bullish', 'bearish', or 'neutral'
    /// </summary>
    [JsonProperty("implication")]
    public string Implication { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Suggested strategic action
    /// </summary>
    [JsonProperty("suggestedAction")]
    public string? SuggestedAction { get; set; }

    /// <summary>
    /// Full original announcement text
    /// </summary>
    [JsonProperty("rawText")]
    public string RawText { get; set; } = string.Empty;
  }
}