using Bot.Application;
using Bot.Application.Ports;
using Bot.Domain;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Bot.Adapters.Discord;

public sealed class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly ChatMessageHandler _handler;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly string _token;
    private readonly TypingIndicator _typing;
    private readonly BotSettings _botOptions;

    public DiscordGateway(
        ChatMessageHandler handler,
        IOptions<BotSettings> botOptions,
        IOptions<DiscordSettings> discordOptions,
        ILogger<DiscordBotService> logger,
        IServiceProvider services)
    {
        _handler = handler;
        _logger = logger;
        _services = services;
        _botOptions = botOptions.Value;
        _token = discordOptions.Value.Token ?? string.Empty;
        _typing = new TypingIndicator();

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        });

        _interactions = new InteractionService(_client);

        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;
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
        _typing.Dispose();
        await _client.StopAsync();
        await _client.LogoutAsync();
        _client.Dispose();
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

        // Register slash commands
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        try
        {
            var options = _services.GetRequiredService<IOptions<DiscordSettings>>().Value;
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
}

public sealed class DiscordSettings
{
    public string? Token { get; set; }
    public ulong? GuildId { get; set; }
}

internal sealed class TypingIndicator : IDisposable
{
    private readonly Dictionary<ulong, CancellationTokenSource> _sources = new();

    public Task<IDisposable> StartAsync(ISocketMessageChannel channel)
    {
        var id = channel.Id;
        var cts = new CancellationTokenSource();
        lock (_sources)
        {
            _sources[id] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await channel.TriggerTypingAsync();
                    await Task.Delay(TimeSpan.FromSeconds(8), cts.Token);
                }
            }
            catch { /* ignored */ }
        });

        return Task.FromResult<IDisposable>(new Stopper(this, id));
    }

    public void Dispose()
    {
        lock (_sources)
        {
            foreach (var kv in _sources) kv.Value.Cancel();
            _sources.Clear();
        }
    }

    private sealed class Stopper : IDisposable
    {
        private readonly TypingIndicator _owner;
        private readonly ulong _id;
        public Stopper(TypingIndicator owner, ulong id) { _owner = owner; _id = id; }
        public void Dispose()
        {
            lock (_owner._sources)
            {
                if (_owner._sources.TryGetValue(_id, out var cts))
                {
                    cts.Cancel();
                    _owner._sources.Remove(_id);
                }
            }
        }
    }
}
