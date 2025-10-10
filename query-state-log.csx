#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : "C:\\ForkerDemo\\forker.db";
var jobIdFilter = args.Length > 1 ? args[1] : null;

Console.WriteLine($"Querying StateChangeLog from: {dbPath}");
Console.WriteLine();

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

var query = jobIdFilter != null
    ? "SELECT * FROM StateChangeLog WHERE JobId = @jobId ORDER BY Timestamp DESC LIMIT 50"
    : "SELECT * FROM StateChangeLog ORDER BY Timestamp DESC LIMIT 50";

using var command = connection.CreateCommand();
command.CommandText = query;
if (jobIdFilter != null)
{
    command.Parameters.AddWithValue("@jobId", jobIdFilter);
}

using var reader = command.ExecuteReader();

if (!reader.HasRows)
{
    Console.WriteLine("No state change log entries found.");
    return;
}

Console.WriteLine("{0,-5} {1,-38} {2,-10} {3,-10} {4,-15} {5,-15} {6,-23} {7,-10}",
    "ID", "JobId", "EntityType", "EntityId", "OldState", "NewState", "Timestamp", "Duration");
Console.WriteLine(new string('-', 150));

while (reader.Read())
{
    Console.WriteLine("{0,-5} {1,-38} {2,-10} {3,-10} {4,-15} {5,-15} {6,-23} {7,-10}",
        reader["Id"],
        reader["JobId"],
        reader["EntityType"],
        reader["EntityId"] is DBNull ? "" : reader["EntityId"],
        reader["OldState"] is DBNull ? "NULL" : reader["OldState"],
        reader["NewState"],
        reader["Timestamp"],
        reader["DurationMs"] is DBNull ? "" : reader["DurationMs"] + "ms");
}
