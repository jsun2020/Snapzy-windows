using Snapzy.Core.History;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("snapzy-hist").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private string MakeFile(string name) { var p = Path.Combine(_dir, name); File.WriteAllText(p, "x"); return p; }

    [Fact]
    public void AddListDelete_Works()
    {
        var store = new HistoryStore(_dir);
        MakeFile("a.png");
        var e = store.Add("a.png", "image");
        Assert.Single(store.List());
        Assert.Equal("image", store.List()[0].Type);
        Assert.True(File.Exists(store.GetFullPath(e)));
        Assert.True(store.Delete(e.Id));
        Assert.Empty(store.List());
        Assert.False(File.Exists(Path.Combine(_dir, "a.png")));
    }

    [Fact]
    public void List_PersistsAcrossInstances_NewestFirst()
    {
        var store = new HistoryStore(_dir);
        MakeFile("a.png"); MakeFile("b.png");
        var first = store.Add("a.png", "image");
        var second = store.Add("b.png", "image");
        store.SetCreatedUtcForTest(first.Id, DateTime.UtcNow.AddSeconds(-10));
        var reloaded = new HistoryStore(_dir);
        Assert.Equal(new[] { second.Id, first.Id }, reloaded.List().Select(x => x.Id));
    }

    [Fact]
    public void CleanupOlderThan_RemovesOldEntriesAndFiles()
    {
        var store = new HistoryStore(_dir);
        MakeFile("old.png"); MakeFile("new.png");
        var old = store.Add("old.png", "image");
        store.SetCreatedUtcForTest(old.Id, DateTime.UtcNow.AddDays(-40));
        store.Add("new.png", "image");
        Assert.Equal(1, store.CleanupOlderThan(30));
        Assert.Single(store.List());
        Assert.False(File.Exists(Path.Combine(_dir, "old.png")));
        Assert.Equal(0, store.CleanupOlderThan(0)); // 0 = keep forever, no-op
    }
}
