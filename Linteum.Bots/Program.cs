namespace Linteum.Bots;

public class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run <bot-name> [options]");
            Console.WriteLine("Available bots: Cleaner, Munch, VanGogh, VanGogh2, or Xerox");
            Console.WriteLine("  Cleaner: dotnet run cleaner [canvas-name]");
            Console.WriteLine("  Xerox:   dotnet run xerox <canvas-name> <image-file>");
            return;
        }

        string botType = args[0].ToLower();

        BotBase bot = botType switch
        {
            "cleaner" => new CleanerBot(args.Length > 1 ? args[1] : "Default"),
            "munch" => new MunchBot(),
            "vangogh" => new VanGoghBot(),
            "vangogh2" => new VanGogh2Bot(),
            "xerox" => args.Length >= 3
                ? new XeroxBot(args[1], args[2])
                : throw new ArgumentException("Xerox requires: dotnet run xerox <canvas-name> <image-file>"),
            _ => throw new ArgumentException($"Unknown bot: {args[0]}")
        };

        await bot.RunAsync();
    }
}
