using System.Text.Json.Serialization;

namespace MistralClient.Models.Shared;

public sealed class UserMessage : MessageBase
{
   public override string Role => "user";

   [JsonPropertyName("content")]
   public override string? Content { get; set; }
}