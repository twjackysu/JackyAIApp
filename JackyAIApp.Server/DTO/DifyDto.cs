using System.Text.Json.Serialization;

namespace JackyAIApp.Server.DTO
{
  // ── Conversation / message history (Dify Service API) ──────────────────────

  /// <summary>Dify GET /conversations response.</summary>
  public class DifyConversationListResponse
  {
    [JsonPropertyName("data")]
    public List<DifyConversationItem> Data { get; set; } = new();

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
  }

  public class DifyConversationItem
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; set; }
  }

  /// <summary>Dify GET /messages response.</summary>
  public class DifyMessageListResponse
  {
    [JsonPropertyName("data")]
    public List<DifyMessageItem> Data { get; set; } = new();

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
  }

  public class DifyMessageItem
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }
  }
}
