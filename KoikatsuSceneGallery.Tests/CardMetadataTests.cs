using System.Buffers;
using System.Text;
using System.Text.Json;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using MessagePack;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardMetadataTests
{
    private static byte[] Pack(Dictionary<string, object?> value) => MessagePackSerializer.Serialize(value);
    private static byte[] Character(string marker = "【KoiKatuCharaSun】", byte[]? extended = null, long? position = null)
    {
        byte[] parameter = Pack(new() { ["lastname"] = "姓", ["firstname"] = "名", ["nickname"] = "小名", ["sex"] = 1,
            ["personality"] = 39, ["birthMonth"] = 9, ["interest"] = new object[] { 1, "future" }, ["unknownField"] = true });
        byte[] about = Pack(new() { ["version"] = "0.0.0", ["language"] = 2, ["userID"] = "creator-123", ["dataID"] = "data-456" });
        extended ??= Pack(new() { ["plugin.example"] = new object[] { 3, new Dictionary<string, object?> { ["blob"] = new byte[] { 1, 2, 3 }, ["setting"] = "value" } } });
        long offset = 0;
        var parts = new[] { ("Parameter", parameter), ("About", about), ("KKEx", extended) };
        var descriptors = new List<Dictionary<string, object?>>();
        foreach (var (name, bytes) in parts)
        {
            descriptors.Add(new() { ["name"] = name, ["version"] = "0.0.0", ["pos"] = name == "KKEx" ? position ?? offset : offset, ["size"] = (long)bytes.Length });
            offset += bytes.Length;
        }
        byte[] header = Pack(new() { ["lstInfo"] = descriptors.ToArray() });
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(TestFiles.BinaryCard(marker, "1.0.0"));
        writer.Write(0); writer.Write(header.Length); writer.Write(header); writer.Write(offset);
        foreach (var (_, bytes) in parts) writer.Write(bytes);
        writer.Flush(); return stream.ToArray();
    }

    [Theory]
    [InlineData("【KoiKatuChara】", "Koikatu")]
    [InlineData("【KoiKatuCharaS】", "KoikatsuParty")]
    [InlineData("【KoiKatuCharaSP】", "KoikatsuPartySpecialPatch")]
    [InlineData("【KoiKatuCharaSun】", "KoikatsuSunshine")]
    public void ReadsFormatsAndSeparatesSummaryFromExtendedValues(string marker, string type)
    {
        using var dir = new TestDirectory(); var path = dir.Write("card.png", Character(marker));
        var summary = CardMetadataReader.TryRead(path, false)!;
        var full = CardMetadataReader.TryRead(path)!;
        Assert.Equal(type, full.Summary.CardType);
        Assert.Equal("小名", full.Summary.Nickname); Assert.Equal(39, full.Summary.PersonalityId);
        Assert.Equal("plugin.example", Assert.Single(full.Summary.PluginGuids));
        Assert.Equal(3, Assert.Single(full.Summary.Plugins).Version);
        Assert.True(full.Summary.Plugins[0].Size > 0);
        Assert.Equal("1.0.0", full.HeaderVersion);
        Assert.Null(summary.Blocks.Single(b => b.Name == "KKEx").Data);
        var plugin = full.Blocks.Single(b => b.Name == "KKEx").Data!.Field("plugin.example")!;
        Assert.Equal(3L, plugin.Items![0].Integer());
        Assert.Equal("AQID", plugin.Items[1].Field("blob")!.Value);
        Assert.NotNull(full.Blocks.Single(b => b.Name == "Parameter").Data!.Field("unknownField"));
        Assert.Equal(type == "KoikatsuSunshine" ? "creator-123" : null, full.Summary.UserId);
        Assert.Equal(type == "KoikatsuSunshine" ? "0.0.0" : "1.0.0", full.Summary.Version);
    }

    [Fact]
    public void CoordinateTailAndPartialBlocksPreserveName()
    {
        using var dir = new TestDirectory();
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8, true);
        w.Write(TestFiles.BinaryCard("【KoiKatuClothes】", "1.0", "制服"));
        w.Write(2); w.Write(new byte[] { 7, 8 }); w.Write("KKEx"); w.Write(3);
        byte[] ext = Pack(new() { ["madevil.test"] = new object[] { 1, new Dictionary<string, object?>() } });
        w.Write(ext.Length); w.Write(ext); w.Flush();
        var path = dir.Write("coord.png", stream.ToArray());
        var doc = CardMetadataReader.TryRead(path)!;
        Assert.Equal("制服", doc.Summary.Name); Assert.Equal(GameVersion.Unknown, doc.Summary.Game);
        Assert.True(doc.Summary.IsMadevil); Assert.Equal(-1, doc.Summary.Sex);
        path = dir.Write("partial.png", stream.ToArray()[..^2]);
        doc = CardMetadataReader.TryRead(path)!;
        Assert.Equal("制服", doc.Summary.Name); Assert.True(doc.Summary.IsPartial);
    }

    [Theory]
    [InlineData(-1)] [InlineData(long.MaxValue)]
    public void InvalidRangeDoesNotDiscardParameter(long position)
    {
        using var dir = new TestDirectory();
        var doc = CardMetadataReader.TryRead(dir.Write("bad.png", Character(position: position)))!;
        Assert.True(doc.Summary.IsPartial); Assert.Equal("姓 名", doc.Summary.Name);
        Assert.Equal("InvalidBlockRange", doc.Blocks.Single(b => b.Name == "KKEx").Diagnostic);
    }

    [Fact]
    public void ArbitraryMessagePackTypesSurviveJson()
    {
        var buffer = new ArrayBufferWriter<byte>(); var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(2); w.Write(42); w.Write(ulong.MaxValue);
        w.Write("extension"); w.WriteExtensionFormat(new ExtensionResult(5, new byte[] { 0, 255 })); w.Flush();
        var value = MetadataValueReader.Read(buffer.WrittenSpan.ToArray(), default);
        var json = JsonSerializer.Serialize(value, CardMetadataExport.JsonOptions);
        Assert.Contains("18446744073709551615", json);
        Assert.Equal(42L, value.Entries![0].Key.Integer());
        Assert.Equal((sbyte)5, value.Field("extension")!.TypeCode);
        Assert.Contains("AP8=", json);
    }

    [Fact]
    public void ExpansionLimitsAndCancellationAreExplicit()
    {
        var buffer = new ArrayBufferWriter<byte>(); var w = new MessagePackWriter(buffer);
        for (int i = 0; i < 66; i++) w.WriteArrayHeader(1);
        w.WriteNil(); w.Flush();
        Assert.Throws<InvalidDataException>(() => MetadataValueReader.Read(buffer.WrittenSpan.ToArray(), default));
        using var dir = new TestDirectory(); var path = dir.Write("card.png", Character());
        Assert.Throws<OperationCanceledException>(() => CardMetadataReader.TryRead(path, token: new CancellationToken(true)));
    }

    [Fact]
    public void MalformedAndExcessiveExtendedDataRemainPartial()
    {
        using var dir = new TestDirectory();
        var malformed = CardMetadataReader.TryRead(dir.Write("malformed.png", Character(extended: [0xc1])))!;
        Assert.True(malformed.Summary.IsPartial); Assert.Equal("姓 名", malformed.Summary.Name);
        var buffer = new ArrayBufferWriter<byte>(); var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(1); w.Write("deep.plugin");
        for (int i = 0; i < 66; i++) w.WriteArrayHeader(1);
        w.WriteNil(); w.Flush();
        var deep = CardMetadataReader.TryRead(dir.Write("deep.png", Character(extended: buffer.WrittenSpan.ToArray())), false)!;
        Assert.True(deep.Summary.IsPartial);
        Assert.Equal("ExpansionLimit", deep.Blocks.Single(b => b.Name == "KKEx").Diagnostic);
        Assert.Equal("姓 名", deep.Summary.Name);
        buffer = new(); w = new MessagePackWriter(buffer); w.WriteArrayHeader(100_001);
        for (int i = 0; i < 100_001; i++) w.WriteNil();
        w.Flush();
        Assert.Throws<InvalidDataException>(() => MetadataValueReader.Read(buffer.WrittenSpan.ToArray(), default));
    }

    [Fact]
    public void MalformedStringLengthReturnsNull()
    {
        using var dir = new TestDirectory();
        var path = dir.Write("bad-length.png", TestFiles.Png(appended: [100, 0, 0, 0, 255, 255, 255, 255, 255, 255]));
        Assert.Null(CardMetadataReader.TryRead(path));
        Assert.Null(CharacterCardParser.TryParse(path));
        Assert.Null(CoordinateCardParser.TryParse(path));
    }

    [Fact]
    public void OversizedBlockDoesNotAllocateOrHideBasicMetadata()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "large.png");
        using (var stream = File.Create(path))
        using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            w.Write(TestFiles.BinaryCard("【KoiKatuClothes】", "1.0", "large"));
            w.Write(0); w.Write("KKEx"); w.Write(3); w.Write(MetadataValueReader.MaxBlockBytes + 1); w.Flush();
            stream.SetLength(stream.Position + MetadataValueReader.MaxBlockBytes + 1L);
        }
        var doc = CardMetadataReader.TryRead(path)!;
        Assert.Equal("large", doc.Summary.Name); Assert.True(doc.Summary.IsPartial);
        Assert.Equal("BlockSizeLimit", Assert.Single(doc.Blocks).Diagnostic);
    }

    [Fact]
    public async Task FailedAtomicExportPreservesExistingDestination()
    {
        using var dir = new TestDirectory();
        var document = CardMetadataReader.TryRead(dir.Write("card.png", Character()))!;
        string destination = Path.Combine(dir.Path, "occupied");
        Directory.CreateDirectory(destination);
        string sentinel = Path.Combine(destination, "keep.txt"); File.WriteAllText(sentinel, "keep");
        var error = await Record.ExceptionAsync(() => CardMetadataExport.WriteAsync(document, destination, false));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("keep", File.ReadAllText(sentinel));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public void SearchAndFiltersShareCrossFieldAndSemantics()
    {
        var s = new CardMetadataSummary { Name = "Alice", Nickname = "Al", UserId = "USER-123", DataId = "Data-456", Sex = 1, PersonalityId = 39, PluginGuids = ["plugin.guid"] };
        Assert.True(CardMetadataQuery.MatchesText(s, "c:/card.png", "author", null, ["alice", "user-123", "GUID"]));
        Assert.False(CardMetadataQuery.MatchesText(s, "c:/card.png", null, null, ["alice", "missing"]));
        Assert.True(CardMetadataQuery.PassesFilters(s, 1, 39, "PLUGIN.GUID"));
        Assert.False(CardMetadataQuery.PassesFilters(s, 0, 39, null));
        Assert.False(CardMetadataQuery.PassesFilters(null, -1, null, null));
        Assert.True(CardMetadataQuery.PassesFilters(null, null, null, null));
    }

    // The user's own note is searchable, and callers that have no note keep
    // the behaviour they had before the parameter existed.
    [Fact]
    public void SearchIncludesTheUserNoteWithoutChangingCallersThatHaveNone()
    {
        var s = new CardMetadataSummary { Name = "Alice" };

        Assert.True(CardMetadataQuery.MatchesText(s, "c:/card.png", null, null, ["短髮"], "短髮 IF"));
        Assert.False(CardMetadataQuery.MatchesText(s, "c:/card.png", null, null, ["長髮"], "短髮 IF"));
        Assert.True(CardMetadataQuery.MatchesText(s, "c:/card.png", null, null, ["alice", "IF"], "短髮 IF"));

        // Same call without a note behaves exactly as before.
        Assert.True(CardMetadataQuery.MatchesText(s, "c:/card.png", null, null, ["alice"]));
        Assert.False(CardMetadataQuery.MatchesText(s, "c:/card.png", null, null, ["短髮"]));
    }

    [Fact]
    public void NoteMatchesLooksOnlyAtTheNote()
    {
        Assert.True(CardMetadataQuery.NoteMatches("短髮 IF", ["短髮"]));
        Assert.True(CardMetadataQuery.NoteMatches("短髮 IF", ["短髮", "if"]));
        Assert.False(CardMetadataQuery.NoteMatches("短髮 IF", ["短髮", "缺"]));
        Assert.False(CardMetadataQuery.NoteMatches(null, ["短髮"]));
        Assert.False(CardMetadataQuery.NoteMatches("   ", ["短髮"]));

        // No keywords means the user is not searching, so a note cannot
        // surface a card that is otherwise hidden.
        Assert.False(CardMetadataQuery.NoteMatches("短髮 IF", []));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("coordinate")]
    public void CoordinateCsvPreservesSerializedName(string name)
    {
        using var dir = new TestDirectory();
        var path = dir.Write("fallback-must-not-replace-name.png",
            TestFiles.BinaryCard("【KoiKatuClothes】", "0.0.0", name));
        var document = CardMetadataReader.TryRead(path)!;

        Assert.Equal(name, document.Summary.Name);
        var row = CardMetadataExport.ToCsv(document).Split("\r\n")[1];
        // These fixture fields contain no commas; inspect the reference's fourth column.
        Assert.Equal("\"" + name + "\"", row.Split(',')[3]);
    }

    [Fact]
    public async Task ExportsCompatibleCsvAndAtomicJson()
    {
        using var dir = new TestDirectory();
        var document = CardMetadataReader.TryRead(dir.Write("card.png", Character()))!;
        document = document with { Summary = document.Summary with { Name = "a,\"b\"\nc" } };
        var csv = CardMetadataExport.ToCsv(document);
        Assert.StartsWith("\"FileName\",\"Size\",\"CardType\"", csv);
        Assert.Contains("\"a,\"\"b\"\"\nc\"", csv);
        Assert.EndsWith(",\"\",\"\",\"\"\r\n\r\n", csv);
        string path = Path.Combine(dir.Path, "export.csv");
        await CardMetadataExport.WriteAsync(document, path, true);
        Assert.Equal(new byte[] { 255, 254 }, File.ReadAllBytes(path)[..2]);
        string jsonPath = Path.Combine(dir.Path, "export.json");
        await CardMetadataExport.WriteAsync(document, jsonPath, false);
        using var json = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CardMetadataExport.WriteAsync(document, jsonPath, false, new CancellationToken(true)));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
        Assert.Equal("0 B", CardMetadataExport.FormatSize(1023));
        Assert.Equal("1 KB", CardMetadataExport.FormatSize(1024));
    }
}
