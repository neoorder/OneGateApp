using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Data;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Contact> Contacts { get; set; }

    public void EnsureMigrations()
    {
        Migration_AddressBookHistory_20260619();
    }

    void Migration_AddressBookHistory_20260619()
    {
        AddColumnIfMissing("Contacts", "Note", "TEXT NULL");
        AddColumnIfMissing("Contacts", "IsAddressBookEntry", "INTEGER NOT NULL DEFAULT 1");
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
