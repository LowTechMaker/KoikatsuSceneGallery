using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// What the user asked the editor to change. The avatar is two fields so the
/// caller can tell "no picture chosen" from "picture removed".
/// </summary>
internal sealed record LocalSourceEdit(string DisplayName)
{
    /// <summary>Image to store as the avatar, or null when it stays as it is.</summary>
    public string? AvatarImagePath { get; init; }

    public bool ClearAvatar { get; init; }
}

/// <summary>
/// Edits a local source: its name, and the picture that stands in for it.
/// </summary>
/// <remarks>
/// One dialog for all three entry points (the local collection page, the
/// authors page and the author detail page) so the three cannot drift apart.
///
/// Picking a picture swaps the dialog content rather than opening a second
/// dialog: only one ContentDialog can be open at a time, and closing this one
/// to ask a follow-up question would throw away the half-finished edit.
/// </remarks>
internal static class LocalSourceEditor
{
    private static readonly ResourceLoader ResLoader = new();

    /// <summary>How many candidate pictures a picker lists.</summary>
    private const int MaxCandidates = 240;

    private sealed record Candidate(string Path, string Label);

    /// <summary>
    /// The edit the user confirmed, or null when they cancelled or changed
    /// nothing.
    /// </summary>
    /// <param name="cardCandidates">
    /// Cards belonging to this source, offered as pictures. Newest first; the
    /// caller decides what belonging means.
    /// </param>
    /// <param name="authorAvatars">
    /// Avatars of other authors, for a friend who is also a creator the user
    /// follows online.
    /// </param>
    public static async Task<LocalSourceEdit?> EditAsync(
        XamlRoot xamlRoot,
        LocalSourceEntry source,
        IReadOnlyList<string> cardCandidates,
        IReadOnlyList<(string Name, string AvatarPath)> authorAvatars)
    {
        ArgumentNullException.ThrowIfNull(source);

        var name = new TextBox
        {
            Text = source.DisplayName,
            AcceptsReturn = false,
            SelectionStart = source.DisplayName.Length,
        };
        var picture = new PersonPicture
        {
            Width = 72,
            Height = 72,
            DisplayName = source.DisplayName,
            ProfilePicture = Load(source.AvatarPath),
        };

        // Held here rather than applied immediately: nothing touches the disk
        // until the user confirms.
        string? chosenAvatar = null;
        var cleared = false;

        name.TextChanged += (_, _) => picture.DisplayName = name.Text.Trim();

        var pickFile = MakeButton("LocalSources_Edit_PickFile");
        var pickCard = MakeButton("LocalSources_Edit_PickCard");
        var pickAuthor = MakeButton("LocalSources_Edit_PickAuthor");
        var clear = MakeButton("LocalSources_Edit_ClearAvatar");

        pickCard.IsEnabled = cardCandidates.Count > 0;
        pickAuthor.IsEnabled = authorAvatars.Count > 0;
        clear.IsEnabled = source.AvatarPath is not null;

        var fields = new StackPanel { Spacing = 8 };
        fields.Children.Add(Caption("LocalSources_Edit_NameLabel"));
        fields.Children.Add(name);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        header.Children.Add(picture);
        header.Children.Add(fields);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(pickFile);
        buttons.Children.Add(pickCard);
        buttons.Children.Add(pickAuthor);
        buttons.Children.Add(clear);

        var form = new StackPanel { Spacing = 12, Width = 460 };
        form.Children.Add(header);
        form.Children.Add(buttons);
        form.Children.Add(Caption("LocalSources_Edit_FolderNote", wrap: true));

        var host = new ContentControl
        {
            Content = form,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ResLoader.GetString("LocalSources_Edit_Title"),
            Content = host,
            PrimaryButtonText = ResLoader.GetString("LocalSources_Edit_Save"),
            CloseButtonText = ResLoader.GetString("LocalSources_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        void Choose(string? path, bool clearing)
        {
            chosenAvatar = path;
            cleared = clearing;
            picture.ProfilePicture = Load(path);
            clear.IsEnabled = path is not null || (source.AvatarPath is not null && !clearing);
        }

        clear.Click += (_, _) => Choose(null, clearing: true);

        pickFile.Click += (_, _) => Guarded("LocalSource.PickAvatarFile", async () =>
        {
            if (await PickImageFileAsync(xamlRoot).ConfigureAwait(true) is { } picked)
                Choose(picked, clearing: false);
        });

        pickCard.Click += (_, _) => Guarded("LocalSource.PickAvatarCard", async () =>
        {
            var candidates = cardCandidates
                .Take(MaxCandidates)
                .Select(path => new Candidate(path, Path.GetFileNameWithoutExtension(path)))
                .ToArray();
            if (await PickAsync(host, form, "LocalSources_Edit_PickCardTitle", candidates)
                .ConfigureAwait(true) is { } picked)
            {
                Choose(picked, clearing: false);
            }
        });

        pickAuthor.Click += (_, _) => Guarded("LocalSource.PickAvatarAuthor", async () =>
        {
            var candidates = authorAvatars
                .Take(MaxCandidates)
                .Select(author => new Candidate(author.AvatarPath, author.Name))
                .ToArray();
            if (await PickAsync(host, form, "LocalSources_Edit_PickAuthorTitle", candidates)
                .ConfigureAwait(true) is { } picked)
            {
                Choose(picked, clearing: false);
            }
        });

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var edited = name.Text.Trim();
        if (edited.Length == 0)
            edited = source.DisplayName;

        var renamed = !string.Equals(edited, source.DisplayName, StringComparison.Ordinal);
        if (!renamed && chosenAvatar is null && !cleared)
            return null;

        return new LocalSourceEdit(edited)
        {
            AvatarImagePath = chosenAvatar,
            ClearAvatar = cleared,
        };
    }

    /// <summary>
    /// Shows the candidates in place of <paramref name="form"/> and returns the
    /// chosen path, or null when the user went back.
    /// </summary>
    private static async Task<string?> PickAsync(
        ContentControl host,
        UIElement form,
        string titleKey,
        IReadOnlyList<Candidate> candidates)
    {
        var completion = new TaskCompletionSource<string?>();
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 360,
            ItemsSource = candidates,
            ItemTemplate = CandidateTemplate(),
        };
        grid.ItemClick += (_, args) => completion.TrySetResult(
            args.ClickedItem is Candidate candidate ? candidate.Path : null);

        var back = MakeButton("LocalSources_Edit_Back");
        back.Click += (_, _) => completion.TrySetResult(null);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        bar.Children.Add(back);
        bar.Children.Add(new TextBlock
        {
            Text = ResLoader.GetString(titleKey),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var panel = new StackPanel { Spacing = 12, Width = 460 };
        panel.Children.Add(bar);
        panel.Children.Add(grid);

        host.Content = panel;
        try
        {
            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            host.Content = form;
        }
    }

    private static DataTemplate CandidateTemplate()
        => (DataTemplate)XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Width="96" Margin="4" Spacing="4">
                <Image Width="88" Height="88" Stretch="UniformToFill" Source="{Binding Path}" />
                <TextBlock Text="{Binding Label}" TextTrimming="CharacterEllipsis"
                           MaxLines="1" FontSize="11" />
              </StackPanel>
            </DataTemplate>
            """);

    private static void Guarded(string operation, Func<Task> action)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), operation, action);

    private static Button MakeButton(string key)
        => new() { Content = ResLoader.GetString(key) };

    private static TextBlock Caption(string key, bool wrap = false)
        => new()
        {
            Text = ResLoader.GetString(key),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Opacity = 0.8,
            FontSize = 12,
        };

    private static BitmapImage? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return new BitmapImage(new Uri(path)) { DecodePixelWidth = 144 };
        }
        catch (Exception)
        {
            // A picture that will not decode must not take the dialog with it.
            return null;
        }
    }

    private static async Task<string?> PickImageFileAsync(XamlRoot xamlRoot)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        foreach (var extension in LocalSourceStore.AvatarExtensions)
            picker.FileTypeFilter.Add(extension);

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            Microsoft.UI.Win32Interop.GetWindowFromWindowId(
                xamlRoot.ContentIslandEnvironment.AppWindowId));

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
