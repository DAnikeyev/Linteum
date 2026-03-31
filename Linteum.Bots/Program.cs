namespace Linteum.Bots;

public class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please specify a bot to run: Cleaner, Munch, or VanGogh");
            return;
        }

        BotBase bot = args[0].ToLower() switch
        {
            "cleaner" => new CleanerBot(),
            "munch" => new MunchBot(),
            "vangogh" => new VanGoghBot(),
            _ => throw new ArgumentException($"Unknown bot: {args[0]}")
        };

        await bot.RunAsync();
    }
}
