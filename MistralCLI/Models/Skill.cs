using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class Skill
{
   [JsonPropertyName("name")]
   public required string Name { get; set; }

   [JsonPropertyName("description")]
   public string? Description { get; set; }

   [JsonPropertyName("systemPrompt")]
   public string? SystemPrompt { get; set; }

   [JsonPropertyName("tools")]
   public List<Tool>? Tools { get; set; }

   [JsonPropertyName("messages")]
   public List<ChatMessage>? Messages { get; set; }

   [JsonPropertyName("temperature")]
   public float? Temperature { get; set; }

   [JsonPropertyName("fileName")]
   public string? FileName { get; set; }
}

public class SkillManifest
{
   [JsonPropertyName("skills")]
   public List<SkillReference> Skills { get; set; } = new();

   [JsonPropertyName("defaultSkills")]
   public List<string> DefaultSkills { get; set; } = new();
}

public class SkillReference
{
   [JsonPropertyName("name")]
   public required string Name { get; set; }

   [JsonPropertyName("file")]
   public required string File { get; set; }

   [JsonPropertyName("description")]
   public string? Description { get; set; }
}