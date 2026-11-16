using System.Text.Json.Serialization;

namespace MistralAi.Agents.Models;

public class Tool
{
   [JsonRequired]
   public ToolType Type { get; set; }

   [JsonRequired]
   public required FunctionDefinition Function { get; set; }
}

public class FunctionDefinition
{
   [JsonRequired]
   public required string Name { get; set; }

   public string? Description { get; set; }

   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public Dictionary<string, object>? Parameters { get; set; }
}