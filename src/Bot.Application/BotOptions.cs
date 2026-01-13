namespace Bot.Application;

public sealed class BotOptions
{
    public int ReplyMaxChars { get; set; } = 1500;
    public int InputMaxChars { get; set; } = 4000;
}
