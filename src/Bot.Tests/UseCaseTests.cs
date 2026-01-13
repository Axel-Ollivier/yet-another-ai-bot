using Bot.Application;
using Bot.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bot.Tests;

public class UseCaseTests
{
    private static HandleIncomingDiscordMessage CreateSut(int replyMax = 1500, int inputMax = 4000, IGptClient? gpt = null)
    {
        gpt ??= new FakeGptClient();
        var persona = new Persona("You are test persona.");
        var options = Options.Create(new BotOptions { ReplyMaxChars = replyMax, InputMaxChars = inputMax });
        var rateLimiter = new PassThroughRateLimiter();
        return new HandleIncomingDiscordMessage(gpt, persona, rateLimiter, options, NullLogger<HandleIncomingDiscordMessage>.Instance);
    }

    [Fact]
    public async Task Ignores_message_not_from_slash_command()
    {
        var sut = CreateSut();
        var msg = Msg(content: "hello", isSlash: false);
        var res = await sut.HandleAsync(msg, default);
        res.ShouldReply.Should().BeFalse();
    }

    [Fact]
    public async Task Replies_when_slash_command()
    {
        var sut = CreateSut();
        var msg = Msg(content: "hello world", isSlash: true);
        var res = await sut.HandleAsync(msg, default);
        res.ShouldReply.Should().BeTrue();
        res.ReplyText.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Truncates_long_reply()
    {
        var sut = CreateSut(replyMax: 10, gpt: new FakeGptClient(new string('x', 100)));
        var msg = Msg(content: "generate long", isSlash: true);
        var res = await sut.HandleAsync(msg, default);
        res.ShouldReply.Should().BeTrue();
        res.ReplyText!.Length.Should().Be(10);
    }

    private static DiscordMessage Msg(string content, bool isSlash = true, string[]? mentions = null, string botId = "999")
        => new(
            AuthorId: "u1",
            AuthorIsBot: false,
            Content: content,
            ChannelId: "c1",
            GuildId: "g1",
            MessageId: "m1",
            MentionedUserIds: mentions ?? Array.Empty<string>(),
            BotUserId: botId,
            IsSlashCommand: isSlash
        );

    private sealed class FakeGptClient : IGptClient
    {
        private readonly string _text;
        public FakeGptClient(string text = "ok") { _text = text; }
        public Task<GptResponse> GenerateAsync(GptRequest request, CancellationToken ct)
            => Task.FromResult(new GptResponse(_text, request.ConversationId, request.MessageId));
    }

    private sealed class PassThroughRateLimiter : IRateLimiter
    {
        public bool TryAcquire(string key, TimeSpan interval) => true;
    }
}
