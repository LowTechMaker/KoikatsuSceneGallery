using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KoikatsuSceneGallery.XamlTestHost;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using var timeout = new Timer(_ =>
        {
            Console.Error.WriteLine("XAML test host exceeded its deadline");
            Environment.Exit(2);
        }, null, 30000, Timeout.Infinite);
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcher));
                var app = new TestApplication();
                app.UnhandledException += (_, args) => Console.Error.WriteLine(args.Exception);
                if (!dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        RunCollectionChecks();
                        await GalleryBaseChecks.RunAsync();
                        await GalleryLoadChecks.RunAsync();
                        DetailViewNavigationChecks.Run();
                    }
                    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                    finally { app.Exit(); }
                }))
                {
                    Console.Error.WriteLine("XAML test scheduling was rejected");
                    Environment.ExitCode = 1;
                    app.Exit();
                }
            });
            return Environment.ExitCode;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void RunCollectionChecks()
    {
        var first = new Entry(1);
        var second = new Entry(2);
        var third = new Entry(3);
        var source = new ObservableCollection<Entry> { first, second, third };
        var view = new AdvancedCollectionView(source, true) { Filter = item => ((Entry)item).Value % 2 == 1 };
        Require(view.Count == 2 && ReferenceEquals(view[0], first) && ReferenceEquals(view[1], third), "initial filter");
        var fifth = new Entry(5);
        using (view.DeferRefresh())
        {
            source.Remove(first);
            source.Add(fifth);
        }
        Require(view.Count == 2 && ReferenceEquals(view[0], third) && ReferenceEquals(view[1], fifth), "deferred source changes");
        Console.WriteLine("PASS: deferred source changes");

        second.Value = 7;
        view.RefreshFilter();
        Require(view.Count == 3 && ReferenceEquals(view[0], second), "metadata refresh");
        view.SortDescriptions.Add(new SortDescription(nameof(Entry.Value), SortDirection.Descending));
        view.RefreshSorting();
        Require(ReferenceEquals(view[0], second) && ReferenceEquals(view[1], fifth), "sorting after refresh");
        Console.WriteLine("PASS: metadata refresh and sorting");

        source.Clear();
        view.RefreshFilter();
        Require(view.Count == 0, "clear");
        source.Add(first);
        Require(view.Count == 1 && ReferenceEquals(view[0], first), "reuse after clear");
        Console.WriteLine("PASS: clear and reuse");
    }

    private static void Require(bool value, string operation)
    {
        if (!value) throw new InvalidOperationException("Collection check failed: " + operation);
    }
}

internal sealed class Entry(int value) : INotifyPropertyChanged
{
    private int _value = value;
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Value
    {
        get => _value;
        set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
    }
}

// No Window is created and the production App/settings are never initialized.
// Referenced XAML resources need the production type provider even though the
// production App is not constructed. Without it startup raises XamlParseException.
internal sealed class TestApplication : Application, Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
{
    private readonly KoikatsuSceneGallery_XamlTypeInfo.XamlMetaDataProvider _metadata = new();
    public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);
    public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);
    public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}
