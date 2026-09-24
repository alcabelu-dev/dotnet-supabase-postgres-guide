using PostgresSqlRunner;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

internal class Program
{
    public static IConfiguration configuration;

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Postgres SQL Runner - Console");
        Console.WriteLine("---------------------------------\n");

        var configurationbuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        configuration = configurationbuilder.Build();

        //.AddEnvironmentVariables();

        var runner = new SqlRunner();
        try
        {
            await runner.RunInteractiveAsync();
            Console.WriteLine("Ya finalizo este helper para db posthres.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.WriteLine(ex.ToString());
            return 1;
        }
       

    }
}

internal class ProgramSettings
{
    public string choice { get; set; }
    public string createDbAns { get; set; }

}
