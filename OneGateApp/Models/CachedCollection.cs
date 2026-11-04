using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace NeoOrder.OneGate.Models;

public class CachedCollection<T>(IDbContextFactory<CacheDbContext> dbContextFactory, HttpClient httpClient) : ObservableCollection<T> where T : class, IComparable<T>
{
    public event EventHandler? CollectionLoaded;

    static readonly string cache_name = typeof(T).Name.ToLowerInvariant();
    static readonly SemaphoreSlim sync_lock = new(1, 1);
    bool loaded = false;

    public new void Add(T item)
    {
        int index = BinarySearch(item);
        if (index < 0)
            base.InsertItem(~index, item);
        else
            base.SetItem(index, item);
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Add(item);
    }

    public int BinarySearch(T item)
    {
        int low = 0;
        int high = Count - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            int compare = base[mid].CompareTo(item);
            if (compare < 0)
                low = mid + 1;
            else if (compare > 0)
                high = mid - 1;
            else
                return mid;
        }
        return ~low;
    }

    public async Task LoadAsync(string url, TimeSpan duration)
    {
        await sync_lock.WaitAsync();
        try
        {
            var dbContext = await dbContextFactory.CreateInitializedDbContextAsync();
            using (dbContext)
            {
                T[]? cachedItems = null;
                if (!loaded)
                {
                    cachedItems = await dbContext.Set<T>().AsNoTracking().ToArrayAsync();
                    AddRange(cachedItems);
                    loaded = true;
                    CollectionLoaded?.Invoke(this, EventArgs.Empty);
                }
                var state = await dbContext.CacheStates.FindAsync(cache_name);
                if (state is not null && DateTime.SpecifyKind(state.LastUpdatedAt, DateTimeKind.Utc) > DateTime.UtcNow - duration) return;
                var items_new = (await httpClient.GetFromJsonAsync<T[]>(url))!;
                cachedItems ??= await dbContext.Set<T>().AsNoTracking().ToArrayAsync();
                foreach (T item in cachedItems)
                {
                    if (!items_new.Any(p => p.CompareTo(item) == 0))
                        dbContext.Entry(item).State = EntityState.Deleted;
                }
                foreach (T item in items_new)
                {
                    T? cached = cachedItems.FirstOrDefault(p => p.CompareTo(item) == 0);
                    if (cached is null)
                        dbContext.Entry(item).State = EntityState.Added;
                    else if (ShouldReplace(cached, item))
                        dbContext.Entry(item).State = EntityState.Modified;
                }
                if (state is null)
                {
                    state = new CacheState { Name = cache_name };
                    dbContext.CacheStates.Add(state);
                }
                state.LastUpdatedAt = DateTime.UtcNow;
                // SaveChanges commits the cache rows and their freshness marker in one transaction.
                await dbContext.SaveChangesAsync();

                // Keep the displayed collection intact if downloading or saving fails.
                bool cacheNeedsSeeding = cachedItems.Length == 0 && Count > 0;
                for (int i = Count - 1; i >= 0; i--)
                {
                    T item = base[i];
                    if (items_new.Any(p => p.CompareTo(item) == 0)) continue;
                    base.RemoveItem(i);
                }
                foreach (T item in items_new)
                {
                    int index = BinarySearch(item);
                    if (index >= 0)
                    {
                        if (cacheNeedsSeeding || ShouldReplace(base[index], item))
                            base.SetItem(index, item);
                    }
                    else
                    {
                        base.InsertItem(~index, item);
                    }
                }
                CollectionLoaded?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            sync_lock.Release();
        }
    }

    public static async Task InvalidateAsync(IDbContextFactory<CacheDbContext> dbContextFactory)
    {
        await sync_lock.WaitAsync();
        try
        {
            if (!File.Exists(SharedOptions.CacheDbPath)) return;
            using var dbContext = await dbContextFactory.CreateInitializedDbContextAsync();
            await dbContext.CacheStates.Where(p => p.Name == cache_name).ExecuteDeleteAsync();
        }
        finally { sync_lock.Release(); }
    }

    static bool ShouldReplace(T current, T incoming)
    {
        return current is IVersioned currentVersioned
            && incoming is IVersioned incomingVersioned
            && currentVersioned.Version != incomingVersioned.Version;
    }
}
