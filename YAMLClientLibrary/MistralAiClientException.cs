using Microsoft.Win32;
using MistralAi.Agents.Services;
using System;

namespace MistralAi.Agents;

public class MistralAiClientException : Exception
{
   public int? StatusCode { get; }
   public string? ResponseContent { get; }

   public MistralAiClientException(string message, int? statusCode = null, string? responseContent = null, Exception? innerException = null)
       : base(message, innerException)
   {
      StatusCode = statusCode;
      ResponseContent = responseContent;
   }
}
