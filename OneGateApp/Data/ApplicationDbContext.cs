using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Data;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<ContactTransfer> ContactTransfers { get; set; }

    public void EnsureMigrations()
    {
        Migration_AddressBookHistory_20260619();
    }

    void Migration_AddressBookHistory_20260619()
    {
        AddColumnIfMissing("Contacts", "Note", "TEXT NULL");
        AddColumnIfMissing("Contacts", "IsAddressBookEntry", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing("Contacts", "LastUsedAt", "TEXT NULL");
        AddColumnIfMissing("Contacts", "TransferCount", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("Contacts", "LastTransactionHash", "TEXT NULL");

        Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS [ContactTransfers] (
                [Id] INTEGER NOT NULL CONSTRAINT [PK_ContactTransfers] PRIMARY KEY AUTOINCREMENT,
                [Address] TEXT NOT NULL,
                [TransactionHash] TEXT NOT NULL,
                [CreatedAt] TEXT NOT NULL,
                [AssetSymbol] TEXT NULL,
                [Amount] TEXT NULL,
                [Memo] TEXT NULL
            )
            """);
        Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS [IX_ContactTransfers_Address_CreatedAt]
            ON [ContactTransfers] ([Address], [CreatedAt])
            """);
        Database.ExecuteSqlRaw("""
            DELETE FROM [ContactTransfers]
            WHERE [Id] NOT IN (
                SELECT MIN([Id])
                FROM [ContactTransfers]
                GROUP BY [Address], [TransactionHash]
            )
            """);
        Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS [IX_ContactTransfers_Address_TransactionHash]
            ON [ContactTransfers] ([Address], [TransactionHash])
            """);
    }

    void AddColumnIfMissing(string table, string column, string definition)
    {
        string safeTable = SanitizeIdentifier(table);
        string safeColumn = SanitizeIdentifier(column);
#pragma warning disable EF1003
        int count = Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('" + safeTable + "') WHERE name = '" + safeColumn + "'")
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
