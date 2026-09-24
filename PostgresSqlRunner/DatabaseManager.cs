using System;
using System.Collections.Generic;
using System.Text;
using Npgsql;

namespace PostgresSqlRunner
{
    internal class DatabaseManager
    {
        /// <summary>
        /// Creates a database if it does not exist. Uses the provided connection string to connect to a privileged database
        /// (commonly 'postgres') where CREATE DATABASE is allowed.
        /// Returns true if database was created, false if it already existed.
        /// </summary>
        public async Task<bool> CreateDatabaseIfNotExistsAsync(string adminConnectionString, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException(nameof(databaseName));

            using var conn = new NpgsqlConnection(adminConnectionString);
            await conn.OpenAsync();

            // Check existence
            using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn))
            {
                checkCmd.Parameters.AddWithValue("name", databaseName);
                var exists = (await checkCmd.ExecuteScalarAsync()) != null;
                if (exists) return false;
            }

            // Create database
            // Note: CREATE DATABASE cannot be run inside a transaction block so execute directly.
            var createSql = $"CREATE DATABASE \"{EscapeIdentifier(databaseName)}\"";
            using var createCmd = new NpgsqlCommand(createSql, conn);
            await createCmd.ExecuteNonQueryAsync();
            return true;
        }

        private static string EscapeIdentifier(string ident)
        {
            return ident.Replace("\"", "\"\"");
        }

        /// <summary>
        /// Generate a SQL script to create a database (informational).
        /// </summary>
        public string GenerateCreateDatabaseScript(string databaseName, string owner = null)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException(nameof(databaseName));
            var escaped = EscapeIdentifier(databaseName);
            if (string.IsNullOrWhiteSpace(owner))
            {
                return $"CREATE DATABASE \"{escaped}\";";
            }
            else
            {
                var ownerEsc = EscapeIdentifier(owner);
                return $"CREATE DATABASE \"{escaped}\" OWNER \"{ownerEsc}\";";
            }
        }
    }
}
