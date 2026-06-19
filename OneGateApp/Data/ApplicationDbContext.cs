using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Data;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<Banner> Banners { get; set; }
    public DbSet<News> News { get; set; }
    public DbSet<DApp> DApps { get; set; }

    public void EnsureMigrations()
    {
        Migration_AddProperty_DApps_Version_20260611();
        Migration_AddProperty_DApps_GameType_20260619();
    }

    void Migration_AddProperty_DApps_Version_20260611()
    {
        int dappVersionColumns = Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('DApps') WHERE name = 'Version'")
            .AsEnumerable()
            .Single();
        if (dappVersionColumns == 0)
            Database.ExecuteSqlRaw("ALTER TABLE [DApps] ADD COLUMN [Version] INTEGER NOT NULL DEFAULT 0");
    }

    void Migration_AddProperty_DApps_GameType_20260619()
    {
        int dappGameTypeColumns = Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('DApps') WHERE name = 'GameType'")
            .AsEnumerable()
            .Single();
        if (dappGameTypeColumns == 0)
            Database.ExecuteSqlRaw("ALTER TABLE [DApps] ADD COLUMN [GameType] TEXT NULL");
    }
}
