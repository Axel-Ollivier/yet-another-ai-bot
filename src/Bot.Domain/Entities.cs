namespace Bot.Domain;

public sealed record Persona(string Prompt);

public sealed record DiscordMessage(
    string AuthorId,
    bool AuthorIsBot,
    string Content,
    string ChannelId,
    string? GuildId,
    string MessageId,
    IReadOnlyList<string> MentionedUserIds,
    string BotUserId,
    bool isDirectMessage,
    bool IsSlashCommand = false
);

public sealed record GptRequest(
    string PersonaPrompt,
    string UserMessage,
    string ConversationId,
    string MessageId
);

public sealed record GptResponse(
    string Text,
    string ConversationId,
    string MessageId
);

public sealed record BotDecision(
    bool ShouldReply,
    string? ReplyText,
    string? Error
)
{
    public static BotDecision Ignore() => new(false, null, null);
    public static BotDecision Reply(string text) => new(true, text, null);
    public static BotDecision Fail(string error) => new(false, null, error);
}

// Weather domain model (provider-agnostic)
public sealed record WeatherInfo(
    string Place,
    double Latitude,
    double Longitude,
    double Temperature,
    double WindSpeed,
    int WeatherCode,
    string TemperatureUnit,
    string WindUnit
);
