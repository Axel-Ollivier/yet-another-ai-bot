using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bot.Domain;
using Bot.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bot.Adapters.OpenAI;

public sealed class OpenAiGptClient : IGptClient
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiGptClient> _logger;
    private readonly string _apiKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiGptClient(HttpClient http, IOptions<OpenAiOptions> options, ILogger<OpenAiGptClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _apiKey = _options.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY not set; OpenAI calls will fail.");
        }
    }

    public async Task<GptResponse> GenerateAsync(GptRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl), "/v1/chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new ChatCompletionsRequest
        {
            Model = _options.Model,
            Messages = new()
            {
                new("system", Truncate(request.PersonaPrompt, 1000)),
                new("user", Truncate(request.UserMessage, 4000))
            }
        };
        message.Content = JsonContent.Create(payload, options: JsonOptions);

        // Simple retry x2
        HttpResponseMessage? resp = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            resp = await _http.SendAsync(message, ct).ConfigureAwait(false);
            if ((int)resp.StatusCode >= 500)
            {
                _logger.LogWarning("OpenAI 5xx attempt {Attempt}", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
                continue;
            }
            break;
        }

        resp!.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ChatCompletionsResponse>(JsonOptions, ct).ConfigureAwait(false);
        var text = result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

        return new GptResponse(text, request.ConversationId, request.MessageId);
    }

    private static string Truncate(string value, int max)
        => value.Length > max ? value[..max] : value;

    private sealed record ChatCompletionsRequest
    {
        public string Model { get; set; } = string.Empty;
        public List<Message> Messages { get; set; } = new();
    }

    private sealed record Message(string Role, string Content);

    private sealed record ChatCompletionsResponse
    {
        public List<Choice> Choices { get; set; } = new();
    }

    private sealed record Choice
    {
        public Message? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }
}
