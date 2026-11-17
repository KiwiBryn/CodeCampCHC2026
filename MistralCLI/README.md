# YAML client application

The Mistral API key is stored outside the project using .NET user secrets for local development. In Visual Studio, right-click the YAML client project and select **Manage User Secrets**, then add:

```json
{
  "MistralAi:ApiKey": "<your Mistral API key>"
}
```

The console app explicitly loads user secrets in all host environments, so no `DOTNET_ENVIRONMENT` setting is required. User secrets are stored per user and are not encrypted; use them only for development.

For deployed environments, provide the `MistralAi__ApiKey` environment variable through your deployment's secret management mechanism. Environment variables and command-line arguments take precedence over user secrets.

## API errors

API failures include the HTTP status and server response body. Review these details before sharing logs, since validation errors can quote request content. Tool results are recorded after their assistant tool-call messages, including follow-up rounds. Previously saved invalid histories are not repaired automatically: use `/clear` to start a new conversation and `/save` to persist it if the error identifies invalid message ordering.

## Skills

Skill files in `Skills` use camelCase property names, including nested message and tool properties. The skill loader accepts property names case-insensitively and string enum values such as `"role": "assistant"` and `"type": "function"`. These settings apply only to skill files; saved context and `tools.json` retain their existing formats.

## Tool configuration

`tools.json` lists the seven registered file-system tools. The current file loader uses default .NET JSON serialization, so model properties use PascalCase (`Type`, `Function`, `Name`, `Description`, `Parameters`) and `Type` is `0` (`ToolType.Function`). JSON Schema keys inside `Parameters` remain lowercase. The API client converts model properties and enum values to the API's wire format when sending requests. `scan_folder` lists immediate contents; `scan_folder_tree` supports flat, breadth-first, and depth-first scans with depth limits and file/exclusion patterns.
