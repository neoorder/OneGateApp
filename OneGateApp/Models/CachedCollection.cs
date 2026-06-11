using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace NeoOrder.OneGate.Models;

public class CachedCollection<T>(ApplicationDbContext dbContext, HttpClient httpClient) : ObservableCollection<T> where T : class, IComparable<T>
{
    public event EventHandler? CollectionLoaded;

    static readonly string settings_key = $"caching/last_update/{typeof(T).Name.ToLowerInvariant()}";
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
        if (!loaded)
        {
            AddRange(await dbContext.Set<T>().ToArrayAsync());
            loaded = true;
            CollectionLoaded?.Invoke(this, EventArgs.Empty);
        }
        var last_update = await dbContext.Settings.GetAsync<DateTimeOffset>(settings_key);
        if (last_update > DateTimeOffset.UtcNow - duration) return;
        var items_new = (await httpClient.GetFromJsonAsync<T[]>(url))!;
        for (int i = Count - 1; i >= 0; i--)
        {
            T item = base[i];
            if (items_new.Any(p => p.CompareTo(item) == 0)) continue;
            base.RemoveItem(i);
            dbContext.Entry(item).State = EntityState.Deleted;
        }
        foreach (T item in items_new)
        {
            int index = BinarySearch(item);
            if (index >= 0)
            {
                if (ShouldReplace(base[index], item))
                {
                    base.SetItem(index, item);
                    dbContext.Entry(item).State = EntityState.Modified;
                }
            }
            else
            {
                base.InsertItem(~index, item);
                dbContext.Entry(item).State = EntityState.Added;
            }
        }
        CollectionLoaded?.Invoke(this, EventArgs.Empty);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        await dbContext.Settings.PutAsync(settings_key, DateTimeOffset.UtcNow);
    }

    static bool ShouldReplace(T current, T incoming)
    {
        return current is IVersioned currentVersioned
            && incoming is IVersioned incomingVersioned
            && currentVersioned.Version != incomingVersioned.Version;
    }
}
