namespace MarkdownViewer.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"MarkdownViewer.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
            return;

        var directory = new DirectoryInfo(Path);
        foreach (var item in directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            item.Attributes &= ~FileAttributes.ReadOnly & ~FileAttributes.Hidden;
        directory.Attributes &= ~FileAttributes.ReadOnly & ~FileAttributes.Hidden;
        Directory.Delete(Path, recursive: true);
    }
}
