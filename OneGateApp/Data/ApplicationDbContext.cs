using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.Text.Json;

namespace NeoOrder.OneGate.Data;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<ActivityRecord> ActivityRecords { get; set; }

    public void EnsureMigrations()
    {
        Migration_AddressBook_20260619();
        Migration_ActivityRecords_20260705();
    }

    void Migration_AddressBook_20260619()
    {
        AddColumnIfMissing("Contacts", "Note", "TEXT NULL");
    }

    void Migration_ActivityRecords_20260705()
    {
        Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS [ActivityRecords] (
                [Id] INTEGER NOT NULL CONSTRAINT [PK_ActivityRecords] PRIMARY KEY AUTOINCREMENT,
                [Kind] INTEGER NOT NULL,
                [CreatedAt] TEXT NOT NULL,
                [DAppId] INTEGER NULL,
                [DAppName] TEXT NULL,
                [DAppHost] TEXT NULL,
                [TransactionHash] TEXT NULL
            )
            """);

        string? legacyRecords = Settings.Get("activity/records");
        if (legacyRecords is null)
            return;

        try
        {
            ActivityRecord[] records = JsonSerializer.Deserialize<ActivityRecord[]>(legacyRecords, SharedOptions.JsonSerializerOptions) ?? [];
            foreach (ActivityRecord record in records.Where(p => p.CreatedAt != default))
            {
                ActivityRecords.Add(new()
                {
                    Kind = record.Kind,
                    CreatedAt = record.CreatedAt,
                    DAppId = record.DAppId,
                    DAppName = record.DAppName,
                    DAppHost = record.DAppHost,
                    TransactionHash = record.TransactionHash
                });
            }
            SaveChanges();
        }
        catch
        {
            // Activity history is diagnostic; invalid legacy data should not block startup.
        }
        finally
        {
            Settings.Delete("activity/records");
        }
    }

    void AddColumnIfMissing(string table, string column, string definition)
    {
        string safeTable = SanitizeIdentifier(table);
        string safeColumn = SanitizeIdentifier(column);
#pragma warning disable EF1003
        int count = Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info({0}) WHERE name = {1}", safeTable, safeColumn)
            .AsEnumerable()
            .Single();
        if (count == 0)
            Database.ExecuteSqlRaw("ALTER TABLE [" + safeTable + "] ADD COLUMN [" + safeColumn + "] " + definition);
#pragma warning restore EF1003
    }

    static string SanitizeIdentifier(string value)
    {
        if (value.Length == 0 || value.Any(p => !char.IsLetterOrDigit(p) && p != '_'))
            throw new InvalidOperationException("Invalid database identifier.");
        return value;
    }
}
