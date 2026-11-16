namespace MistralAi.Agents.Models;

public enum MessageRole
{
   System,
   User,
   Assistant,
   Tool
}

public enum FinishReason
{
   Stop,
   Length,
   ToolCalls,
   ModelLength
}

public enum ToolType
{
   Function
}

public enum ToolChoice
{
   Auto,
   None,
   Any,
   Required
}

public enum ResponseFormatType
{
   Text,
   JsonObject
}