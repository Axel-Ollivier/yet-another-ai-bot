# Yet Another AI Bot (.NET 8, Hexagonal)

Discord bot built with .NET 8 in a hexagonal architecture (Domain, Application, Adapters, Host). The bot exposes slash commands:
- /ask: sends your prompt to OpenAI with a Persona
- /meteo: fetches current weather from Open‑Meteo

## Prerequisites
- .NET SDK 8.0+
- Discord Bot token
- OpenAI API key (for /ask)

## Secrets (file-based)
Create a file at repo root: [secrets.json](secrets.json)

```json
{
   "Discord": {
      "Token": "YOUR_DISCORD_BOT_TOKEN",
      "GuildId": 0
   },
   "Gpt": {
      "ApiKey": "YOUR_OPENAI_API_KEY"
   }
}
```

GuildId optional: if set to your server ID, slash commands appear immediately on that guild; otherwise they are registered globally (may take time to propagate).

## Configuration (appsettings.json)
File: [src/Bot.Host/appsettings.json](src/Bot.Host/appsettings.json)
- Bot:
   - ReplyMaxChars (default 1500)
   - InputMaxChars (default 4000)
- Gpt:
   - Model (e.g., gpt-4o-mini)
   - BaseUrl (default https://api.openai.com/v1)
   - ApiKey (prefer putting it in secrets.json)
- Discord:
   - Token (prefer in secrets.json)
   - GuildId (optional)
- Persona:
   - Prompt (default system persona). You can override by placing a persona.txt alongside the executable.

## How to run
1) Build
- dotnet build
2) Run
- dotnet run --project src/Bot.Host

On startup, slash commands register to your GuildId (if configured) or globally.

## Commands
- /ask "question" → calls OpenAI via `IGptClient` and replies concisely
- /meteo "ville" → calls `IWeatherClient` (Open‑Meteo adapter) and returns a formatted weather embed

## Architecture (hexagonal)
- Domain (pure models):
   - [src/Bot.Domain/Entities.cs](src/Bot.Domain/Entities.cs)
   - Persona, DiscordMessage, GptRequest/GptResponse, BotDecision, WeatherInfo
- Application (use cases + contracts):
   - Use case: [src/Bot.Application/HandleIncomingDiscordMessage.cs](src/Bot.Application/HandleIncomingDiscordMessage.cs)
   - Interfaces (ports): [src/Bot.Application/Interfaces.cs](src/Bot.Application/Interfaces.cs) → IGptClient, IRateLimiter, IWeatherClient
- Adapters:
   - Inbound: Discord → [src/Bot.Adapters.Discord](src/Bot.Adapters.Discord)
      - Host service, slash commands (/ask, /meteo), rate limiter
   - Outbound: OpenAI → [src/Bot.Adapters.OpenAI](src/Bot.Adapters.OpenAI) (HTTP)
   - Outbound: Open‑Meteo → [src/Bot.Adapters.OpenMeteo](src/Bot.Adapters.OpenMeteo) (HTTP)
- Host:
   - Wiring/DI/Config → [src/Bot.Host/Program.cs](src/Bot.Host/Program.cs)

Clean rules:
- Domain has no SDK dependencies (Discord/OpenAI/Open‑Meteo only in adapters)
- Application targets ports (interfaces), not concrete adapters
- Adapters implement ports and map SDK/HTTP DTOs to domain models

## Projects
- [src/Bot.Domain](src/Bot.Domain)
- [src/Bot.Application](src/Bot.Application)
- [src/Bot.Adapters.Discord](src/Bot.Adapters.Discord)
- [src/Bot.Adapters.OpenAI](src/Bot.Adapters.OpenAI)
- [src/Bot.Adapters.OpenMeteo](src/Bot.Adapters.OpenMeteo)
- [src/Bot.Host](src/Bot.Host)
- [src/Bot.Tests](src/Bot.Tests)

## Tests
- dotnet test

## Notes
- DI via Microsoft.Extensions.DependencyInjection
- Logging via Microsoft.Extensions.Logging
- Simple per-user rate limit (1 req/5s) in-memory
