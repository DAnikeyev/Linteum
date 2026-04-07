namespace Linteum.Bots;

public class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run <bot-name> [canvas-name-for-cleaner]");
            Console.WriteLine("Available bots: Cleaner, Munch, VanGogh, or VanGogh2");
            return;
        }

        string botType = args[0].ToLower();

        BotBase bot = botType switch
        {
            "cleaner" => new CleanerBot(args.Length > 1 ? args[1] : "Default"),
            "munch" => new MunchBot(),
            "vangogh" => new VanGoghBot(),
            "vangogh2" => new VanGogh2Bot(),
            _ => throw new ArgumentException($"Unknown bot: {args[0]}")
        };

        await bot.RunAsync();
    }
}
