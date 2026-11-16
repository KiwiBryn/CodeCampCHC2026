using System.Text.Json.Serialization;

namespace MistralClient.Models.Shared;

public sealed class ToolMessage : MessageBase
{
   public override string Role => "tool";

   [JsonPropertyName("content")]
   public override string? Content { get; set; }

   [JsonPropertyName("tool_call_id")]
   public override string? ToolCallId { get; set; }
}