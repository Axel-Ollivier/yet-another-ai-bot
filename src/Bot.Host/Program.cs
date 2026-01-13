using Bot.Adapters.Discord;
using Bot.Adapters.OpenAI;
using Bot.Adapters.OpenMeteo;
using Bot.Application;
using Bot.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true)
           .AddJsonFile("secrets.json", optional: true)
           .AddJsonFile("appsettings.Local.json", optional: true)
           .AddEnvironmentVariables();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        services.Configure<BotOptions>(config.GetSection("Bot"));
        services.Configure<OpenAiOptions>(config.GetSection("Gpt"));
        services.Configure<DiscordOptions>(config.GetSection("Discord"));

        // Persona load from config then override by persona.txt if present
        var persona = new Persona(config["Persona:Prompt"] ?? "You are a helpful assistant. Be concise and safe.");
        var personaPath = Path.Combine(AppContext.BaseDirectory, "persona.txt");
        if (File.Exists(personaPath))
        {
            var txt = File.ReadAllText(personaPath);
            if (!string.IsNullOrWhiteSpace(txt))
            {
                persona = new Persona(txt);
            }
        }
        services.AddSingleton(persona);

        // HttpClient for OpenAI
        services.AddHttpClient<OpenAiGptClient>(client =>
        {
            var baseUrl = config["Gpt:BaseUrl"] ?? "https://api.openai.com/v1";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddTransient<IGptClient, OpenAiGptClient>();

        // HttpClient for Open-Meteo
        services.AddHttpClient<OpenMeteoClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IWeatherClient, OpenMeteoClient>();

        services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();

        services.AddSingleton<HandleIncomingDiscordMessage>();
        services.AddHostedService<DiscordHostService>();
    });

var app = builder.Build();
await app.RunAsync();
