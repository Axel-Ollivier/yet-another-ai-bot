FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and all sources (simpler, less cache-efficient)
COPY yet-another-ai-bot.sln ./
COPY src/ ./src/

# Restore and publish Host only
RUN dotnet restore src/Bot.Host/Bot.Host.csproj
RUN dotnet publish src/Bot.Host/Bot.Host.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Bot.Host.dll"]
