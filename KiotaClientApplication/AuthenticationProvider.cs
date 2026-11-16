using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

public sealed class BearerAuthenticationProvider
    : IAuthenticationProvider
{
   private readonly string _apiKey;

   public BearerAuthenticationProvider(string apiKey)
   {
      _apiKey = apiKey;
   }

   public Task AuthenticateRequestAsync(
       RequestInformation request,
       Dictionary<string, object>? additionalAuthenticationContext = null,
       CancellationToken cancellationToken = default)
   {
      request.Headers.Add(
          "Authorization",
          $"Bearer {_apiKey}");

      return Task.CompletedTask;
   }
}