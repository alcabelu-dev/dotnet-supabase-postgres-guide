// name=SqlRunner.cs
using Npgsql;
using PostgresSqlRunner;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class SqlRunner
{
    private const string LogsDir = "logs";
    private readonly IConfiguration _configuration;

    public async Task RunInteractiveAsync()
    {
        var programSettings = Program.configuration.GetSection("program-choice");
        // add package for net10 instalar Binder
        // dotnet add package Microsoft.Extensions.Configuration.Binder
        //var programSettings = Program.configuration.GetSection("program-choice").Get<ProgramSettings>();

        Console.WriteLine("Choose connection target:");
        Console.WriteLine("1) Docker / Local Postgres");
        Console.WriteLine("2) Supabase Alcabelu / Hosted Postgres");
        Console.WriteLine("3) Supabase peter Custom connection string");
        Console.Write("Enter 1/2/3: ");
        //var choice = Console.ReadLine()?.Trim();
        //var choice = "1"; // For testing, default to 1
        //leer from appsettings.json
        var choice= programSettings["choice"];


        string connectionString;
        if (choice == "1")
        {
            Console.WriteLine("Selected 1) Docker / Local Postgres");
            connectionString = AskLocalDockerConnection();
        }
        else if (choice == "2")
        {
            Console.WriteLine("Selected 2) Supabase Alcabelu / Hosted Postgres");
            connectionString = AskSupabaseConnection();
           
        }
        else
        {
            connectionString = AskCustomConnectionString();
        }

        connectionString = connectionString?.Trim();
        var adminConnectionString = GetAdminConnectionString(connectionString);

        Console.Write("Do you want to create a database (or ensure it exists)? (y/N): ");
        
        //var createDbAns = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        //var createDbAns = "y"; // For testing, default to y
        var createDbAns = programSettings["createDbAns"] ?? "n"; // For testing, default to n    
        if (createDbAns == "y" || createDbAns == "yes")
        {
            Console.Write("Enter database name to create/ensure: ");
           // var dbName = (Console.ReadLine() ?? "").Trim();
            var dbName = programSettings["dbName"] ?? "qardeala"; // For testing, default to qardeala

            if (string.IsNullOrEmpty(dbName))
            {
                Console.WriteLine("No database name provided — skipping create.");
            }
            else
            {
                await TryCreateDatabase(adminConnectionString, dbName);
                // If user asked to create a database with a different name than the one in connection string,
                // update connectionString to point to the new db:
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                builder.Database = dbName;
                connectionString = builder.ConnectionString;
            }
        }

        Console.Write("Path to folder with SQL files: ");
        //var path = (Console.ReadLine() ?? "").Trim();
        // indicate for number 1 create schemas / and table auth
        // indicate for number 2 all tables for shema auth donot need in supabase
        // indicate for number 2 all tables for qareal core
        // indicate for number 3 schema and table for ssis
        // indicate for number 4 stored procedure for ssis

        var choicepath = programSettings["choicepath"] ?? "1"; //create schemas Core
        string path = String.Empty;

        if (choicepath == "1")
        {
            Console.WriteLine("Selected 1) Create Schemas qardeal core");
            path = programSettings["choicepath-1"] ?? "1"; //create schemas Core
            //path = ("D:\\qardeal\\database\\migrations").Trim();
        }
        else if (choicepath == "2")
        {
            Console.WriteLine("Selected 2) Create auth tables /do not used in supebase");
            //path = ("D:\\repo\\qardeal\\database\\migrations").Trim();
            path = programSettings["choicepath-2"] ?? "1";
        }
        else if (choicepath == "3")
        {
            path = programSettings["choicepath-3"] ?? "1";
        }
        else if (choice == "4")
        {
            path = programSettings["choicepath-4"] ?? "1";
        }

        while (!Directory.Exists(path))
        {
            Console.Write($"Folder doesn't exist: {path}. Enter valid folder path (or 'quit'): ");
            path = (Console.ReadLine() ?? "").Trim();
            if (path?.ToLowerInvariant() == "quit") return;
        }

        var sqlFiles = GetSqlFilesSorted(path);
        if (sqlFiles.Count == 0)
        {
            Console.WriteLine("No .sql files found in folder.");
            return;
        }

        Console.WriteLine("\nFound SQL files:");
        for (int i = 0; i < sqlFiles.Count; i++)
        {
            Console.WriteLine($"{i + 1,3}: {Path.GetFileName(sqlFiles[i])}");
        }

        Console.Write("\nRun (A)ll or (S)elect files? (A/S): ");
        var runChoice = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
        List<int> selectedIndices;
        if (runChoice == "A" || runChoice == "ALL")
        {
            selectedIndices = Enumerable.Range(1, sqlFiles.Count).ToList();
        }
        else
        {
            Console.Write("Enter comma-separated indexes or ranges (e.g. 1,3-5): ");
            var selection = (Console.ReadLine() ?? "").Trim();
            selectedIndices = ParseSelection(selection, sqlFiles.Count);
        }

        Directory.CreateDirectory(LogsDir);
        var logFilePath = Path.Combine(LogsDir, $"execution_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        using var logWriter = new StreamWriter(logFilePath);
        await logWriter.WriteLineAsync($"# Execution log - {DateTime.Now:O}");
        await logWriter.WriteLineAsync($"Target connection: {MaskConnectionString(connectionString)}");
        await logWriter.WriteLineAsync($"SQL folder: {path}");
        await logWriter.WriteLineAsync("");

        Console.WriteLine($"\nExecuting {selectedIndices.Count} files...");
        using var conn = new NpgsqlConnection(connectionString?.Trim());
        try { 
        await conn.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open connection: {ex.Message}");
            await logWriter.WriteLineAsync($"Failed to open connection: {ex.Message}");
            return;
        }

        foreach (var idx in selectedIndices)
        {
            if (idx < 1 || idx > sqlFiles.Count) continue;
            var filePath = sqlFiles[idx - 1];
            var fileName = Path.GetFileName(filePath);
            var sql = await File.ReadAllTextAsync(filePath);

            Console.WriteLine($"\nExecuting [{idx}] {fileName} ...");
            await logWriter.WriteLineAsync($"## {fileName}");
            await logWriter.WriteLineAsync($"Start: {DateTime.Now:O}");
            await logWriter.WriteLineAsync($"Path: {filePath}");
            try
            {
                var resultText = await ExecuteSqlFileAsync(conn, sql);
                Console.WriteLine($"SUCCESS: {fileName}");
                await logWriter.WriteLineAsync("Result: SUCCESS");
                await logWriter.WriteLineAsync("Details:");
                await logWriter.WriteLineAsync("```");
                await logWriter.WriteLineAsync(resultText);
                await logWriter.WriteLineAsync("```");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {fileName} -> {ex.Message}");
                await logWriter.WriteLineAsync("Result: FAILED");
                await logWriter.WriteLineAsync("Error:");
                await logWriter.WriteLineAsync("```");
                await logWriter.WriteLineAsync(ex.ToString());
                await logWriter.WriteLineAsync("```");
            }
            await logWriter.WriteLineAsync($"End: {DateTime.Now:O}\n");
            await logWriter.FlushAsync();
        }

        Console.WriteLine($"\nExecution completed. Log written to {logFilePath}");
    }

    private static string AskLocalDockerConnection()
    {
        var databaselocal = Program.configuration.GetSection("database-local");
        
        Console.Write("Host (default: localhost): ");
        //var host = ReadWithDefault("localhost");
        //var host = "localhost";
        var host = databaselocal["host"] ?? "localhost";

        Console.Write("Port (default: 5432): ");
        //var port = ReadWithDefault("5432");
        var port = databaselocal["port"] ?? "5432";

        Console.Write("Username (default: postgres): ");
        //var user = ReadWithDefault("postgres");
        var user = databaselocal["user"] ?? "postgres";

        Console.Write("Password (leave blank to use none): ");
        //var pass = Console.ReadLine() ?? "";
        ///var pass = "postgres123";
        var pass = databaselocal["password"] ?? "postgres123";

        Console.Write("Database (default: postgres): ");
        //var db = ReadWithDefault("postgres");
        //var db = "qardeala";
        var db = databaselocal["database"] ?? "postgres";


        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.Parse(port),
            Username = user,
            Password = pass,
            Database = db,
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }

    private static string AskSupabaseConnection()
    {
        
        var databasesupabasealcabelu = Program.configuration.GetSection("database-supabase-alcabelu");

        Console.WriteLine("Enter Supabase connection details.");
        Console.WriteLine("You can paste the full connection string, or enter parts.");
        Console.Write("Paste full connection string? (y/N): ");
        //var kop = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        var kop = databasesupabasealcabelu["kop"] ?? "n";
        if (kop == "y" || kop == "yes")
        {
            Console.Write("Connection string: ");
            var con = databasesupabasealcabelu["Connection"] ?? "";
            return con?.Trim() ?? "";            
        }

        Console.Write("Host (supabase host): ");
        //var host = Console.ReadLine() ?? "";
        var host = databasesupabasealcabelu["host"] ?? "localhost";
        Console.Write("Port (default 5432): ");
        //var port = ReadWithDefault("5432");
        var port = databasesupabasealcabelu["port"] ?? "5432";
        Console.Write("Database: ");
        //var db = Console.ReadLine() ?? "";
        var db = databasesupabasealcabelu["database"] ?? "postgres";
        Console.Write("Username: ");
        var user = databasesupabasealcabelu["username"] ?? "";
        Console.Write("Password: ");
        var pass = databasesupabasealcabelu["password"] ?? "";

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.Parse(port),
            Username = user,
            Password = pass,
            Database = db,
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }

    private static string AskCustomConnectionString()
    {
        Console.Write("Enter connection string: ");
        return Console.ReadLine() ?? "";
    }

    private static string ReadWithDefault(string def)
    {
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return def;
        return input.Trim();
    }

    private static List<string> GetSqlFilesSorted(string folder)
    {
        var files = Directory.GetFiles(folder, "*.sql", SearchOption.TopDirectoryOnly).ToList();
        var regex = new Regex(@"^\s*(\d+)", RegexOptions.Compiled);

        return files
            .Select(f => new
            {
                Path = f,
                Order = regex.Match(Path.GetFileName(f)) is Match m && m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue,
                Name = Path.GetFileName(f)
            })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Path)
            .ToList();
    }

    private static List<int> ParseSelection(string input, int max)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(input)) return result;
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Contains('-'))
            {
                var rng = t.Split('-', 2);
                if (int.TryParse(rng[0], out var a) && int.TryParse(rng[1], out var b))
                {
                    var from = Math.Max(1, Math.Min(a, b));
                    var to = Math.Min(max, Math.Max(a, b));
                    for (int i = from; i <= to; i++) result.Add(i);
                }
            }
            else if (int.TryParse(t, out var v))
            {
                if (v >= 1 && v <= max) result.Add(v);
            }
        }
        return result.Distinct().OrderBy(x => x).ToList();
    }

    private static string MaskConnectionString(string cs)
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(cs);
            if (!string.IsNullOrEmpty(b.Password)) b.Password = "****";
            return b.ConnectionString;
        }
        catch
        {
            if (cs.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            {
                return Regex.Replace(cs, @"(Password\s*=\s*)([^;]+)", "$1****", RegexOptions.IgnoreCase);
            }
            return cs;
        }
    }

    private static string GetAdminConnectionString(string cs)
    {
        // Use provided connection string but ensure admin/default database is "postgres" where CREATE DATABASE can be executed.
        try
        {
            var b = new NpgsqlConnectionStringBuilder(cs);
            if (string.IsNullOrWhiteSpace(b.Database) || b.Database.Equals("postgres", StringComparison.OrdinalIgnoreCase))
            {
                b.Database = "postgres";
                return b.ConnectionString;
            }
            // if current user has rights on their DB but you still want to create another DB, we use 'postgres' database.
            b.Database = "postgres";
            return b.ConnectionString;
        }
        catch
        {
            return cs; // best-effort
        }
    }

    private async Task TryCreateDatabase(string adminConnectionString, string dbName)
    {
        Console.WriteLine($"Ensuring database '{dbName}' exists (using admin connection)...");
        var mgr = new DatabaseManager();
        var created = await mgr.CreateDatabaseIfNotExistsAsync(adminConnectionString, dbName);
        if (created)
            Console.WriteLine($"Database '{dbName}' created.");
        else
            Console.WriteLine($"Database '{dbName}' already exists or could not be created.");
    }

    private async Task<string> ExecuteSqlFileAsync(NpgsqlConnection openConn, string sql)
    {
        // Try to execute inside a transaction for atomic per-file execution; if fails due to statements that cannot run in transactions,
        // re-run without transaction.
        try
        {
            using var tx = await openConn.BeginTransactionAsync();
            using var cmd = new NpgsqlCommand(sql, openConn, tx);
            cmd.CommandTimeout = 0; // large/indefinite
            var affected = await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return $"Executed inside transaction. Rows affected (approx): {affected}";
        }
        catch (PostgresException pex) when (IsNotAllowedInTransaction(pex))
        {
            // Retry without transaction
            using var cmd = new NpgsqlCommand(sql, openConn);
            cmd.CommandTimeout = 0;
            var affected = await cmd.ExecuteNonQueryAsync();
            return $"Executed without transaction due to non-transactional command. Rows affected (approx): {affected}";
        }
        catch (Exception ex)
        {
            // Bubble up
            throw new InvalidOperationException("Failed to execute file: " + ex.Message, ex);
        }
    }

    private static bool IsNotAllowedInTransaction(PostgresException pex)
    {
        // common message: "CREATE DATABASE cannot be executed in a transaction block."
        if (pex.SqlState == "25001") return true; // SQLSTATE for active SQL transaction problem (may vary)
        var m = pex.Message?.ToLowerInvariant() ?? "";
        if (m.Contains("cannot be executed in a transaction", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Contains("cannot run inside a transaction", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}