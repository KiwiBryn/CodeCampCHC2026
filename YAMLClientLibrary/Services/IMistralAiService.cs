using MistralAi.Agents.Models;

namespace MistralAi.Agents.Services;

public interface IMistralAiService
{
   Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

   IAsyncEnumerable<ChatCompletionStreamResponse> CreateChatCompletionStreamAsync(
       ChatCompletionRequest request,
       CancellationToken cancellationToken = default);
}