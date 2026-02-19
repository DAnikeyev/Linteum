using System.Net.Http.Json;
using Linteum.Shared.DTO;

namespace Linteum.Bots;

public class Program
{
    static async Task Main(string[] args)
    {
        var bot = new VanGoghBot();
        await bot.RunAsync();
    }
}
