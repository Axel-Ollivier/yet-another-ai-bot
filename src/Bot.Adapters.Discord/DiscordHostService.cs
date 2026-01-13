using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Bot.Domain;
using Bot.Application;

namespace Bot.Adapters.Discord;

public sealed class DiscordHostService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly ILogger<DiscordHostService> _logger;
    private readonly string _token;

    public DiscordHostService(
        IOptions<DiscordOptions> discordOptions,
        IServiceProvider services,
        ILogger<DiscordHostService> logger)
    {
        _services = services;
        _logger = logger;
        _token = discordOptions.Value.Token ?? string.Empty;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        });
        _interactions = new InteractionService(_client);

        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            _logger.LogError("Discord token not configured. Set 'Discord:Token' in appsettings.json or secrets.json.");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task OnLogAsync(LogMessage arg)
    {
        var level = arg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };
        _logger.Log(level, arg.Exception, "[Discord] {Message}", arg.Message);
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Discord connected as {Name} ({Id})", _client.CurrentUser.Username, _client.CurrentUser.Id);

        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        try
        {
            var options = _services.GetRequiredService<IOptions<DiscordOptions>>().Value;
            if (options.GuildId.HasValue && options.GuildId.Value != 0)
            {
                await _interactions.RegisterCommandsToGuildAsync(options.GuildId.Value, deleteMissing: true);
                _logger.LogInformation("Slash commands registered to guild {GuildId}.", options.GuildId);
            }
            else
            {
                await _interactions.RegisterCommandsGloballyAsync();
                _logger.LogInformation("Slash commands registered globally (may take up to 1h to appear). Consider setting Discord:GuildId during dev.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var ctx = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(ctx, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while executing interaction");
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                try { await interaction.RespondAsync("Sorry, something went wrong.", ephemeral: true); } catch { /* ignored */ }
            }
        }
    }

    private Task OnMessageReceivedAsync(SocketMessage raw)
    {
        if (raw is not SocketUserMessage msg) return Task.CompletedTask;
        if (msg.Author.Id == _client.CurrentUser.Id || msg.Author.IsBot) return Task.CompletedTask;

        var channel = msg.Channel;
        // Permission guard: if guild channel, ensure we can send messages
        if (channel is SocketGuildChannel gch)
        {
            var self = gch.Guild.CurrentUser;
            var perms = self.GetPermissions(gch);
            if (!perms.SendMessages)
            {
                _logger.LogWarning("Missing SendMessages permission in channel {ChannelId}", gch.Id);
                return Task.CompletedTask;
            }
        }

        _ = Task.Run(async () =>
        {
            var guildId = (channel as SocketGuildChannel)?.Guild.Id.ToString();
            var mentions = msg.MentionedUsers.Select(u => u.Id.ToString()).ToList();

            var dm = new DiscordMessage(
                AuthorId: msg.Author.Id.ToString(),
                AuthorIsBot: msg.Author.IsBot,
                Content: msg.Content,
                ChannelId: channel.Id.ToString(),
                GuildId: guildId,
                MessageId: msg.Id.ToString(),
                MentionedUserIds: mentions,
                BotUserId: _client.CurrentUser.Id.ToString(),
                IsSlashCommand: false
            );

            try
            {
                var handler = _services.GetRequiredService<HandleIncomingDiscordMessage>();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var decision = await handler.HandleAsync(dm, cts.Token);
                if (!decision.ShouldReply) return;
                var reply = decision.ReplyText ?? string.Empty;
                await channel.SendMessageAsync(reply);
            }
            catch (global::Discord.Net.HttpException httpEx)
            {
                _logger.LogWarning("Cannot send message in channel {ChannelId}: {Reason}", channel.Id, httpEx.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message received");
            }
        });

        return Task.CompletedTask;
    }
}