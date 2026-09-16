using System.Text.Json;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class AtomicJsonFileTests
{
    [Fact]
    public async Task ReplacesExistingDocumentUsingCallerOptionsAndRemovesTemporaryFile()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        File.WriteAllText(path, "old document");
        var document = new Document("新名稱");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        await AtomicJsonFile.WriteAsync(path, document, options, default);
        Assert.Equal(JsonSerializer.Serialize(document, options), File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SerializationFailurePreservesOldDocumentAndCleansTemporaryFile()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "document.json");
        File.WriteAllText(path, "old document");
        var expected = new InvalidOperationException("serializer failed");
        var options = Options(() => throw expected);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicJsonFile.WriteAsync(path, new Document("new"), options, default));
        Assert.Same(expected, error);
        Assert.Equal("old document", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrDuringSerializationPreservesOldDocument(bool duringSerialization)
    {
        using var directory = new TestDirectory();
        using var cancellation = new CancellationTokenSource();
        var path = Path.Combine(directory.Path, "document.json");
        File.WriteAllText(path, "old document");
        bool entered = false;
        var options = Options(() => { entered = true; cancellation.Cancel(); });
        if (!duringSerialization) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AtomicJsonFile.WriteAsync(path, new Document("new"), options, cancellation.Token));
        if (duringSerialization) Assert.True(entered);
        Assert.Equal("old document", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task FailedReplacementCleansTemporaryFileAndLeavesDestinationIntact()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "destination.json");
        Directory.CreateDirectory(path);
        var sentinel = Path.Combine(path, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        var error = await Record.ExceptionAsync(() =>
            AtomicJsonFile.WriteAsync(path, new Document("new"), new JsonSerializerOptions(), default));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Expected replacement failure");
        Assert.Empty(Directory.GetFiles(directory.Path));
        Assert.Equal("keep", File.ReadAllText(sentinel));
    }

    private static JsonSerializerOptions Options(Action onWrite)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ControlledConverter(onWrite));
        return options;
    }

    private sealed record Document(string DisplayName);
    private sealed class ControlledConverter(Action onWrite) : JsonConverter<Document>
    {
        public override Document? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Document value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.DisplayName);
            onWrite();
        }
    }
}
