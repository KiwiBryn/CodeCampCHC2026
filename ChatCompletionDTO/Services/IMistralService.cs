using MistralClient.Models.Shared;
using MistralClient.Models.Streaming;
using MistralClient.Models.Synchronous;

namespace MistralClient.Services;

public interface IMistralService
{
   Task<ChatCompletionResponse> GetChatCompletionAsync(
       List<MessageBase> messages,
       string? model = null,
       double? temperature = null,
       int? maxTokens = null,
       bool? stream = false,
       CancellationToken cancellationToken = default);

   IAsyncEnumerable<StreamingResponse> StreamChatCompletionAsync(
       List<MessageBase> messages,
       string? model = null,
       double? temperature = null,
       int? maxTokens = null,
       CancellationToken cancellationToken = default);
}