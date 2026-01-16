using Bot.Application;
using Bot.Domain;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Bot.Adapters.Discord;

public class DiscordCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly HandleIncomingDiscordMessage _handler;
    private readonly IWeatherClient _weather;

    public DiscordCommands(HandleIncomingDiscordMessage handler, IWeatherClient weather)
    {
        _handler = handler;
        _weather = weather;
    }

    [SlashCommand("ask", "Ask the bot a question")]
    public async Task Ask([Summary(description: "Your question")] string prompt)
    {
        await DeferAsync();

        var msg = new DiscordMessage(
            AuthorId: Context.User.Id.ToString(),
            AuthorIsBot: Context.User.IsBot,
            Content: prompt,
            ChannelId: Context.Channel.Id.ToString(),
            GuildId: (Context.Guild?.Id).ToString(),
            MessageId: Context.Interaction.Id.ToString(),
            MentionedUserIds: Array.Empty<string>(),
            BotUserId: Context.Client.CurrentUser.Id.ToString(),
            isDirectMessage: Context.Channel.GetType() == typeof(SocketDMChannel),
            IsSlashCommand: true
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var decision = await _handler.HandleAsync(msg, cts.Token);

        if (!decision.ShouldReply)
        {
            await FollowupAsync("No reply.");
            return;
        }

        var reply = decision.ReplyText ?? string.Empty;
        await FollowupAsync(reply);
    }

    [SlashCommand("meteo", "Obtiens la météo actuelle d'une localisation (Open‑Meteo)")]
    public async Task Meteo([Summary(description: "Ville ou lieu, ex: Paris")] string location)
    {
        await DeferAsync();
        try
        {
            var info = await _weather.GetCurrentAsync(location, CancellationToken.None);
            if (info is null)
            {
                await FollowupAsync("Lieu introuvable ou service indisponible.");
                return;
            }

            var (label, emoji) = MapWeather(info.WeatherCode);
            var color = info.Temperature switch
            {
                >= 30 => new Color(0xF39C12),
                >= 20 => new Color(0x27AE60),
                >= 10 => new Color(0x3498DB),
                >= 0 => new Color(0x2E86C1),
                _ => new Color(0x5DADE2)
            };

            var eb = new EmbedBuilder()
                .WithTitle($"{emoji} Météo à {info.Place}")
                .WithColor(color)
                .AddField("Température", double.IsNaN(info.Temperature) ? "—" : $"{info.Temperature:F1} {info.TemperatureUnit}", inline: true)
                .AddField("Vent", double.IsNaN(info.WindSpeed) ? "—" : $"{info.WindSpeed:F0} {info.WindUnit}", inline: true)
                .AddField("Conditions", string.IsNullOrWhiteSpace(label) ? "—" : label, inline: true)
                .WithFooter("Source: open-meteo.com");

            await FollowupAsync(embed: eb.Build());
        }
        catch (Exception ex)
        {
            await FollowupAsync($"Erreur: {ex.Message}");
        }
    }

    private static (string Label, string Emoji) MapWeather(int code) => code switch
    {
        0 => ("Ciel dégagé", "☀️"),
        1 or 2 => ("Partiellement nuageux", "🌤️"),
        3 => ("Couvert", "☁️"),
        45 or 48 => ("Brouillard", "🌫️"),
        51 or 53 or 55 => ("Bruine", "🌦️"),
        56 or 57 => ("Bruine verglaçante", "🌧️"),
        61 or 63 or 65 => ("Pluie", "🌧️"),
        66 or 67 => ("Pluie verglaçante", "🌧️❄️"),
        71 or 73 or 75 => ("Neige", "❄️"),
        77 => ("Grains de neige", "❄️"),
        80 or 81 or 82 => ("Averses", "🌦️"),
        85 or 86 => ("Averses de neige", "🌨️"),
        95 => ("Orage", "⛈️"),
        96 or 97 => ("Orage avec grêle", "⛈️🧊"),
        _ => ($"Code météo {code}", "🌡️")
    };
}
