using MistralClient.Models.Shared;
using MistralClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddHttpClient<IMistralService, MistralService>();
services.AddSingleton<IConfiguration>(configuration);

var serviceProvider = services.BuildServiceProvider();
var mistralService = serviceProvider.GetRequiredService<IMistralService>();

// Read system message
var systemMessageContent = await File.ReadAllTextAsync("system.md");
var systemMessage = new SystemMessage { Content = systemMessageContent };

// Initialize conversation with system message
var messages = new List<MessageBase> { systemMessage };

Console.WriteLine("Mistral AI Chat Client (Enter blank line to exit)");
Console.WriteLine("-----------------------------------------------");

while (true)
{
   Console.Write("You: ");
   var userInput = Console.ReadLine();

   if (string.IsNullOrWhiteSpace(userInput))
      break;

   // Add user message to conversation
   messages.Add(new UserMessage { Content = userInput });

   string yaml = await File.ReadAllTextAsync("mistral-ai-chat-completions-api-openapi.yaml");

   messages.Add(new UserMessage { Content = yaml });

   try
   {
      // Get completion
      //var response = await mistralService.GetChatCompletionAsync(messages);
      var response = await mistralService.GetChatCompletionAsync(messages, temperature: 0.7, maxTokens: 5096);

      if (response.Choices != null && response.Choices.Count > 0)
      {
         var assistantMessage = response.Choices[0].Message;
         if (assistantMessage != null)
         {
            // Add assistant response to conversation
            messages.Add(assistantMessage);

            Console.WriteLine($"Assistant: {assistantMessage.Content}");
         }
      }

      if (response?.Usage is { } usage)
      {
         Console.WriteLine($"Tokens used: Prompt={usage.PromptTokens}, Completion={usage.CompletionTokens}, Total={usage.TotalTokens}");
      }
   }
   catch (Exception ex)
   {
      Console.WriteLine($"Error: {ex.Message}");
   }
}

Console.WriteLine("Chat session ended.");