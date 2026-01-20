using System.Text.Json.Serialization;

namespace JackyAIApp.Server.DTO
{
  /// <summary>
  /// Request model for Dify Chat API
  /// </summary>
  public class DifyChatRequest
  {
    [JsonPropertyName("inputs")]
    public Dictionary<string, string> Inputs { get; set; } = new();

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("response_mode")]
    public string ResponseMode { get; set; } = "blocking";

    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<object> Files { get; set; } = new();
  }

  /// <summary>
  /// Response model for Dify Chat API
  /// </summary>
  public class DifyChatResponse
  {
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("metadata")]
    public DifyMetadata? Metadata { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }
  }

  /// <summary>
  /// Metadata from Dify response
  /// </summary>
  public class DifyMetadata
  {
    [JsonPropertyName("usage")]
    public DifyUsage? Usage { get; set; }

    [JsonPropertyName("retriever_resources")]
    public List<object>? RetrieverResources { get; set; }
  }

  /// <summary>
  /// Token usage information from Dify
  /// </summary>
  public class DifyUsage
  {
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("prompt_unit_price")]
    public string? PromptUnitPrice { get; set; }

    [JsonPropertyName("prompt_price_unit")]
    public string? PromptPriceUnit { get; set; }

    [JsonPropertyName("prompt_price")]
    public string? PromptPrice { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("completion_unit_price")]
    public string? CompletionUnitPrice { get; set; }

    [JsonPropertyName("completion_price_unit")]
    public string? CompletionPriceUnit { get; set; }

    [JsonPropertyName("completion_price")]
    public string? CompletionPrice { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("total_price")]
    public string? TotalPrice { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("latency")]
    public double Latency { get; set; }
  }
}
