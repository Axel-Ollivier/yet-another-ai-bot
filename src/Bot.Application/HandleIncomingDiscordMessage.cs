using Bot.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bot.Application;

public sealed class HandleIncomingDiscordMessage
{
    private readonly IGptClient _gpt;
    private readonly Persona _persona;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<HandleIncomingDiscordMessage> _logger;
    private readonly BotOptions _options;

    public HandleIncomingDiscordMessage(
        IGptClient gpt,
        Persona persona,
        IRateLimiter rateLimiter,
        IOptions<BotOptions> options,
        ILogger<HandleIncomingDiscordMessage> logger)
    {
        _gpt = gpt;
        _persona = persona;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<BotDecision> HandleAsync(DiscordMessage msg, CancellationToken ct)
    {
        var content = msg.Content ?? string.Empty;

        // Accept if slash command, if bot is mentioned or if it's a direct message
        var isMentioned = msg.MentionedUserIds.Contains(msg.BotUserId);
        if (!(msg.IsSlashCommand || isMentioned || msg.isDirectMessage))
            return BotDecision.Ignore();

        if (!msg.IsSlashCommand)
        {
            if (isMentioned)
            {
                content = RemoveMention(content, msg.BotUserId);
            }
        }

        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return BotDecision.Ignore();

        if (content.Length > _options.InputMaxChars)
        {
            content = content[.._options.InputMaxChars];
        }

        if (!_rateLimiter.TryAcquire(msg.AuthorId, TimeSpan.FromSeconds(5)))
        {
            return BotDecision.Reply("Too many requests, please wait a few seconds.");
        }

        try
        {
            var request = new GptRequest(
                _persona.Prompt,
                content,
                msg.GuildId ?? msg.ChannelId,
                msg.MessageId
            );

            var response = await _gpt.GenerateAsync(request, ct).ConfigureAwait(false);
            var text = (response.Text ?? string.Empty).Trim();
            if (text.Length > _options.ReplyMaxChars)
                text = text[.._options.ReplyMaxChars];

            return BotDecision.Reply(text);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GPT call canceled for message {MessageId}", msg.MessageId);
            return BotDecision.Fail("Canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling message {MessageId}", msg.MessageId);
            return BotDecision.Reply("Sorry, something went wrong.");
        }
    }

    private static string RemoveMention(string content, string botUserId)
    {
        // Discord mention formats: <@id> or <@!id>
        var id = botUserId;
        content = content.Replace($"<@{id}>", string.Empty, StringComparison.OrdinalIgnoreCase);
        content = content.Replace($"<@!{id}>", string.Empty, StringComparison.OrdinalIgnoreCase);
        return content;
    }
}
