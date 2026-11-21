using System.IO.Compression;
using System.Text;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(Directory.CreateTempSubdirectory("archive").FullName);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] ZipBytes(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        return stream.ToArray();
    }

    private static byte[] ModBytes(string guid)
        => ZipBytes(("manifest.xml", Encoding.UTF8.GetBytes($"<manifest><guid>{guid}</guid></manifest>")));

    private string Archive(params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(_root, "pack.zip");
        File.WriteAllBytes(path, ZipBytes(entries));
        return path;
    }

    private string Staging => Path.Combine(_root, "staging");

    [Fact]
    public void CardsAndModsComeOutWithTheirFoldersAndOtherFilesAreLeftIn()
    {
        var archive = Archive(
            ("pack/scene/a.png", [1]),
            ("pack/mods/m.zipmod", ModBytes("com.m")),
            ("pack/lazy.zip", ModBytes("com.lazy")),
            ("pack/readme.txt", [2]),
            ("pack/empty/", []));

        var result = ArchiveExtractor.Extract(archive, Staging);

        Assert.Equal(
            ["pack/scene/a.png", "pack/mods/m.zipmod", "pack/lazy.zip"],
            result.Files.Select(f => Path.GetRelativePath(Staging, f.Path).Replace('\\', '/')));
        Assert.All(result.Files, f => Assert.Equal(archive, f.ArchivePath));
        Assert.Equal(1, result.SkippedEntries);
        Assert.Equal("com.lazy", ZipmodManifestReader.TryRead(result.Files[2].Path)!.Guid);
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public void AnArchiveInsideIsUnpackedOneLevelAndThenRemoved()
    {
        var inner = ZipBytes(("b.png", [3]), ("deeper.zip", ZipBytes(("c.png", [4]))));
        var archive = Archive(("inner.zip", inner));

        var result = ArchiveExtractor.Extract(archive, Staging);

        Assert.Equal(["inner/b.png"], result.Files.Select(f => Path.GetRelativePath(Staging, f.Path).Replace('\\', '/')));
        Assert.False(File.Exists(Path.Combine(Staging, "inner.zip")));
        Assert.Equal(1, result.SkippedEntries); // the archive two levels down
    }

    [Fact]
    public void APathThatWouldLeaveTheStagingFolderStaysInside()
    {
        var archive = Archive(("../../evil.png", [5]), ("C:/abs.png", [6]));

        var result = ArchiveExtractor.Extract(archive, Staging);

        Assert.All(result.Files, f => Assert.StartsWith(Staging + Path.DirectorySeparatorChar, f.Path));
        Assert.False(File.Exists(Path.Combine(_root, "evil.png")));
        Assert.Equal(2, result.Files.Count);
    }

    [Fact]
    public void TwoEntriesThatSanitiseToOneNameBothSurvive()
    {
        var archive = Archive(("a?.png", [7]), ("a_.png", [8]));

        var result = ArchiveExtractor.Extract(archive, Staging);

        Assert.Equal(2, result.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
