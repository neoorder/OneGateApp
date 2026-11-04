using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Data;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<BiometricCredential> BiometricCredentials { get; set; }
    public DbSet<DAppFavorite> DAppFavorites { get; set; }
    public DbSet<DAppRecentVisit> DAppRecentVisits { get; set; }
    public DbSet<HiddenToken> HiddenTokens { get; set; }
    public DbSet<ExcludedNewsCategory> ExcludedNewsCategories { get; set; }
    public DbSet<ChatPin> ChatPins { get; set; }
    public DbSet<ChatDraft> ChatDrafts { get; set; }
    public DbSet<UnsentChatMessage> UnsentChatMessages { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<ActivityRecord> ActivityRecords { get; set; }
}
