using System.Diagnostics;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;
using Xunit.Sdk;

namespace KoikatsuSceneGallery.PluginSmokeTests;

public sealed class PluginPackagingSmokeTests
{
    private static readonly PluginExpectation[] Expectations =
    [
        new(
            "PixivAuthorsPlugin",
            "SceneGallery.Plugin.PixivAuthors.csproj",
            "PixivAuthors",
            "Pixiv Authors",
            "Resolves Pixiv author info, artwork metadata, and reverse image search",
            "https://github.com/LowTechMaker/pixiv-data-plugin",
            [
                typeof(IFolderAuthorProvider),
                typeof(ICardImportProvider),
                typeof(IImportDestinationProvider),
                typeof(IReverseImageSearchProvider),
                typeof(IPluginSettingsProvider),
            ]),
        new(
            "BepisDbPlugin",
            "SceneGallery.Plugin.BepisDb.csproj",
            "BepisDb",
            "BepisDB",
            "Imports card metadata from BepisDB (db.bepis.moe)",
            "https://github.com/LowTechMaker/BepisDbPlugin",
            [
                typeof(IFolderAuthorProvider),
                typeof(ICardImportProvider),
                typeof(IImportDestinationProvider),
                typeof(ICookieSetupValidator),
                typeof(IPluginSettingsProvider),
            ]),
        new(
            "FanboxWebView2Plugin",
            "SceneGallery.Plugin.FanboxWebView2.csproj",
            "Fanbox",
            "Fanbox",
            "Imports pixivFANBOX metadata through a WebView2 browser context",
            "https://github.com/lowtechmaker/FanboxWebView2Plugin",
            [
                typeof(IFolderAuthorProvider),
                typeof(ICardImportProvider),
                typeof(IImportDestinationProvider),
                typeof(IPluginSettingsProvider),
            ]),
        new(
            "GitHubReleaseUpdatePlugin",
            "SceneGallery.Plugin.GitHubReleaseUpdates.csproj",
            "GitHubReleaseUpdates",
            "GitHub Release Updates",
            "Checks plugin updates from GitHub Releases",
            null,
            [typeof(IPluginUpdateProvider)]),
    ];

    public static TheoryData<PluginExpectation> Plugins
    {
        get
        {
            var data = new TheoryData<PluginExpectation>();
            foreach (var expectation in Expectations)
                data.Add(expectation);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Plugins))]
    public void PublishedPlugin_LoadsWithExpectedMetadataAndCapabilities(PluginExpectation expected)
    {
        var appRepo = FindAppRepo();
        var workspaceRoot = Directory.GetParent(appRepo)!.FullName;
        var pluginRepo = Path.Combine(workspaceRoot, expected.RepoDirectory);
        var projectPath = Path.Combine(pluginRepo, expected.ProjectFile);

        if (!Directory.Exists(pluginRepo) || !File.Exists(projectPath))
            throw SkipException.ForSkip($"Sibling plugin repo is missing: {expected.RepoDirectory}");

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "SceneGallery.PluginSmokeTests",
            expected.OutputDirectory,
            Guid.NewGuid().ToString("N"));
        var pluginsRoot = Path.Combine(testRoot, "Plugins");
        var pluginOutput = Path.Combine(pluginsRoot, expected.OutputDirectory);
        var storageRoot = Path.Combine(testRoot, "Storage");

        Directory.CreateDirectory(pluginOutput);

        PluginService? service = null;
        try
        {
            PublishPlugin(projectPath, appRepo, pluginOutput);
            Assert.False(File.Exists(Path.Combine(pluginOutput, "SceneGallery.PluginSdk.dll")));
            Assert.False(File.Exists(Path.Combine(pluginOutput, "KoikatsuSceneGallery.Core.dll")));

            var logger = new RecordingLogger();
            service = new PluginService(logger, storageRoot, pluginsRoot);
            service.LoadPlugins();

            AssertPlugin(service, expected);
            Assert.Empty(logger.Errors);
        }
        finally
        {
            service?.Shutdown();
            TryDeleteDirectory(testRoot);
        }
    }

    [Fact]
    public void PublishedPluginSet_LoadsAllFourPluginsTogether()
    {
        var appRepo = FindAppRepo();
        var workspaceRoot = Directory.GetParent(appRepo)!.FullName;
        var missing = Expectations
            .Where(expected => !File.Exists(Path.Combine(
                workspaceRoot,
                expected.RepoDirectory,
                expected.ProjectFile)))
            .Select(expected => expected.RepoDirectory)
            .ToList();

        if (missing.Count > 0)
            throw SkipException.ForSkip($"Sibling plugin repos are missing: {string.Join(", ", missing)}");

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "SceneGallery.PluginSmokeTests",
            "AllPlugins",
            Guid.NewGuid().ToString("N"));
        var pluginsRoot = Path.Combine(testRoot, "Plugins");
        var storageRoot = Path.Combine(testRoot, "Storage");

        PluginService? service = null;
        try
        {
            foreach (var expected in Expectations)
            {
                var projectPath = Path.Combine(
                    workspaceRoot,
                    expected.RepoDirectory,
                    expected.ProjectFile);
                var pluginOutput = Path.Combine(pluginsRoot, expected.OutputDirectory);
                Directory.CreateDirectory(pluginOutput);
                PublishPlugin(projectPath, appRepo, pluginOutput);
            }

            var logger = new RecordingLogger();
            service = new PluginService(logger, storageRoot, pluginsRoot);
            service.LoadPlugins();

            Assert.Equal(Expectations.Length, service.Plugins.Count);
            foreach (var expected in Expectations)
                AssertPlugin(service, expected);
            Assert.Empty(logger.Errors);
        }
        finally
        {
            service?.Shutdown();
            TryDeleteDirectory(testRoot);
        }
    }

    private static void PublishPlugin(string projectPath, string appRepo, string outputDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
        {
            "publish", projectPath,
            "--no-restore",
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "false",
            "-p:Platform=x64",
            "-p:DeployPluginToApp=false",
            $"-p:SceneGalleryAppDir={appRepo}",
            "-o", outputDirectory,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start dotnet publish.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"dotnet publish failed ({process.ExitCode}).{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static void AssertCapability(PluginService service, string pluginName, Type capability)
    {
        if (capability == typeof(IFolderAuthorProvider))
            Assert.Contains(service.AuthorProviders, provider => provider.Name == pluginName);
        else if (capability == typeof(ICardImportProvider))
            Assert.Contains(service.ImportProviders, provider => provider.Name == pluginName);
        else if (capability == typeof(IImportDestinationProvider))
            Assert.IsAssignableFrom<IImportDestinationProvider>(
                Assert.Single(service.ImportProviders, provider => provider.Name == pluginName));
        else if (capability == typeof(ICookieSetupValidator))
            Assert.IsAssignableFrom<ICookieSetupValidator>(
                Assert.Single(service.CookieSetupProviders, provider => provider.Name == pluginName));
        else if (capability == typeof(IReverseImageSearchProvider))
            Assert.Equal(pluginName, Assert.IsAssignableFrom<IPlugin>(service.ReverseImageSearchProvider).Name);
        else if (capability == typeof(IPluginSettingsProvider))
            Assert.NotNull(service.GetSettingsProvider(pluginName));
        else if (capability == typeof(IPluginUpdateProvider))
            Assert.Contains(service.UpdateProviders, provider => provider.Name == pluginName);
        else
            throw new InvalidOperationException($"Unsupported capability assertion: {capability.FullName}");
    }

    private static void AssertPlugin(PluginService service, PluginExpectation expected)
    {
        var plugin = Assert.Single(service.Plugins, info => info.Name == expected.Name);
        Assert.Equal(PluginStatus.Loaded, plugin.Status);
        Assert.Null(plugin.Error);
        Assert.NotEqual("?", plugin.Version);
        Assert.Equal(expected.Description, plugin.Description);
        Assert.Equal(expected.UpdateUrl, plugin.UpdateUrl);

        foreach (var capability in expected.Capabilities)
            AssertCapability(service, expected.Name, capability);
    }

    private static string FindAppRepo()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KoikatsuSceneGallery.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the KoikatsuSceneGallery repo root.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup: a native plugin dependency may remain mapped briefly.
        }
    }

    public sealed record PluginExpectation(
        string RepoDirectory,
        string ProjectFile,
        string OutputDirectory,
        string Name,
        string Description,
        string? UpdateUrl,
        IReadOnlyList<Type> Capabilities);

    private sealed class RecordingLogger : IAppLogger
    {
        public List<(string Operation, Exception Exception, string? Path)> Errors { get; } = [];

        public void LogError(string operation, Exception exception, string? path = null)
            => Errors.Add((operation, exception, path));
    }
}
