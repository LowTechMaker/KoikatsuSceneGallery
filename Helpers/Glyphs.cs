namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Every icon the app draws in code (Segoe Fluent Icons), one name per meaning.
/// XAML reads the same values from the <c>Glyph*</c> strings in Styles/Tokens.xaml.
/// </summary>
/// <remarks>
/// Wave 3 of centralising values (user request). Before it, the same meaning
/// was drawn with two icons (queue: Pin in the details pane, Add in the menus;
/// hide: Clear and Hide; import: Add and Download), one icon meant two things
/// (Contact was both an author and a character), and about fifty icons were
/// written as raw private-use characters, which show as blanks in an editor
/// and in a diff. Use the name; never write a code point at a call site.
/// </remarks>
internal static class Glyphs
{
    // ------------------------------------------------------------ pages and kinds of card
    public const string Home = "\uE80F";
    public const string Library = "\uE8B9";
    /// <summary>A scene card (the library's own picture icon).</summary>
    public const string Scene = "\uE8B9";
    /// <summary>A character card. Emoji2, not Contact: Contact is the author.</summary>
    public const string Character = "\uE76E";
    public const string Coordinate = "\uE78A";
    /// <summary>One author.</summary>
    public const string Author = "\uE77B";
    /// <summary>The authors page (people, plural).</summary>
    public const string Authors = "\uE716";
    public const string LocalSources = "\uE8F4";
    public const string Discovery = "\uE8B1";
    public const string Shuffle = "\uE8B1";
    public const string Rating = "\uE734";
    public const string Tags = "\uE8EC";
    public const string Usage = "\uE81C";
    public const string Record = "\uE7C8";
    public const string Import = "\uE896";
    public const string Settings = "\uE713";
    public const string Posts = "\uE8F1";

    // ------------------------------------------------------------ a card's state (the same icon on the pane, the menus and the badges)
    public const string Queue = "\uE718";
    public const string Like = "\uEB51";
    public const string Hide = "\uED1A";
    public const string Selected = "\uE73E";
    /// <summary>The joker in a dealt hand (random discovery).</summary>
    public const string Joker = "\uE8EF";

    // ------------------------------------------------------------ actions
    public const string Open = "\uE8A7";
    public const string OpenFolder = "\uED43";
    public const string CopyFile = "\uE8C8";
    public const string CopyLink = "\uE71B";
    public const string Add = "\uE710";
    public const string Remove = "\uE711";
    public const string Delete = "\uE74D";
    public const string Edit = "\uE70F";
    public const string Refresh = "\uE72C";
    public const string Web = "\uE774";
    public const string Search = "\uE721";
    public const string Filter = "\uE71C";
    public const string Sort = "\uE8CB";
    public const string SortDescending = "\uE74B";
    public const string SortAscending = "\uE74A";
    public const string More = "\uE712";
    public const string Close = "\uE711";
    public const string Back = "\uE72B";
    public const string Previous = "\uE76B";
    public const string Next = "\uE76C";
    public const string ChevronDown = "\uE70D";
    public const string ZoomIn = "\uE8A3";
    public const string ZoomOut = "\uE71F";
    public const string DetailsPane = "\uE8A0";
    public const string Thumbnail = "\uE91B";
    public const string Folder = "\uE8B7";
    /// <summary>"This becomes that" (an import conflict: new file, then the one it would replace).</summary>
    public const string ArrowRight = "\uE72A";
    public const string Done = "\uE930";
    public const string Warning = "\uE7BA";
    public const string Error = "\uEA39";
    /// <summary>Already in the library (an import row), and a post's page of works.</summary>
    public const string InLibrary = "\uE8F1";
}
