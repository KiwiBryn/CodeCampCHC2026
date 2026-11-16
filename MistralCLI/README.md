# YAML client application

The Mistral API key is stored outside the project using .NET user secrets for local development. In Visual Studio, right-click the YAML client project and select **Manage User Secrets**, then add:

```json
{
  "MistralAi:ApiKey": "<your Mistral API key>"
}
```

The console app explicitly loads user secrets in all host environments, so no `DOTNET_ENVIRONMENT` setting is required. User secrets are stored per user and are not encrypted; use them only for development.

For deployed environments, provide the `MistralAi__ApiKey` environment variable through your deployment's secret management mechanism. Environment variables and command-line arguments take precedence over user secrets.
