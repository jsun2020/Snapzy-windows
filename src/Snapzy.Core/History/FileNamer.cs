namespace Snapzy.Core.History;

public static class FileNamer
{
    public static string NewCaptureName(DateTime now, string ext) =>
        $"Snapzy {now:yyyy-MM-dd HH.mm.ss}.{ext}";
}
