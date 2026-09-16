using System.Diagnostics;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class XamlCollectionHostTests
{
    [Fact]
    public async Task RealXamlCollectionAndGalleryBaseSupportRefreshSortingAndReuseWithoutWindow()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "XamlHost", "KoikatsuSceneGallery.XamlTestHost.exe");
        Assert.True(File.Exists(path), "The XAML test host must be built and copied with the integration tests.");
        using var process = new Process
        {
            StartInfo = new(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            }
        };
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode == 0, $"XAML host exit={process.ExitCode}\n{output}\n{error}");
            Assert.Contains("PASS: deferred source changes", output);
            Assert.Contains("PASS: metadata refresh and sorting", output);
            Assert.Contains("PASS: clear and reuse", output);
            Assert.Contains("PASS: gallery visible random choice and isolated resolution state", output);
            Assert.Contains("PASS: gallery shuffle resolution updates", output);
            Assert.Contains("PASS: gallery shuffle rotation", output);
            Assert.Contains("PASS: gallery empty random choice", output);
            Assert.Contains("PASS: gallery thumbnail session restart", output);
            Assert.Contains("PASS: gallery load setup and awaited batch", output);
            Assert.Contains("PASS: gallery overlapping load cancellation isolation", output);
            Assert.Contains("PASS: gallery load failure cleanup and retry", output);
            Assert.Contains("PASS: gallery scanned cards preserve order duplicates and cancellation", output);
            Assert.Contains("PASS: gallery disposal cancels sources and permits load cleanup", output);
            Assert.Contains("PASS: detail view navigation follows visible order", output);
            Assert.Contains("PASS: detail view random and removal candidates", output);
            Assert.Contains("PASS: detail view empty single and mismatched type", output);
            Assert.Contains("PASS: detail view scope selection", output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }
}
