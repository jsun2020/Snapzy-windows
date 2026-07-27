using System.Text.Json;

namespace Snapzy.Core.History;

public class HistoryStore
{
    private readonly string _dir;
    private readonly string _indexFile;
    private readonly object _lock = new();
    private List<HistoryEntry> _entries;

    public HistoryStore(string capturesDir)
    {
        _dir = capturesDir;
        Directory.CreateDirectory(_dir);
        _indexFile = Path.Combine(_dir, "history.json");
        _entries = LoadIndex();
    }

    private List<HistoryEntry> LoadIndex()
    {
        if (!File.Exists(_indexFile)) return new();
        try { return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_indexFile)) ?? new(); }
        catch (Exception) { return new(); }
    }

    private void SaveIndex()
    {
        var tmp = _indexFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _indexFile, overwrite: true);
    }

    public IReadOnlyList<HistoryEntry> List()
    { lock (_lock) return _entries.OrderByDescending(e => e.CreatedUtc).ToList(); }

    public HistoryEntry Add(string fileName, string type)
    {
        lock (_lock)
        {
            var e = new HistoryEntry { FileName = fileName, Type = type };
            _entries.Add(e);
            SaveIndex();
            return e;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e is null) return false;
            var path = GetFullPath(e);
            if (File.Exists(path)) File.Delete(path);
            _entries.Remove(e);
            SaveIndex();
            return true;
        }
    }

    public int CleanupOlderThan(int days)
    {
        if (days <= 0) return 0;
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var stale = _entries.Where(e => e.CreatedUtc < cutoff).ToList();
            var removed = 0;
            foreach (var e in stale)
            {
                var path = GetFullPath(e);
                try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { continue; }
                _entries.Remove(e);
                removed++;
            }
            if (removed > 0) SaveIndex();
            return removed;
        }
    }

    public string GetFullPath(HistoryEntry e) => Path.Combine(_dir, e.FileName);

    public void SetCreatedUtcForTest(string id, DateTime utc)
    {
        lock (_lock)
        {
            var e = _entries.First(x => x.Id == id);
            e.CreatedUtc = utc;
            SaveIndex();
        }
    }
}
