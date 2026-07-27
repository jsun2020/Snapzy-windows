namespace Snapzy.Core.History;

public class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string Type { get; set; } = "image"; // image | video | gif
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
