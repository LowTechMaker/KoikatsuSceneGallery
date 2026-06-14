namespace SceneGallery.PluginSdk;

/// <summary>
/// Optional interface for plugins that need browser cookies (e.g. Cloudflare
/// cf_clearance) to access their backend. The host app shows a WebView2 popup
/// where the user completes challenges, then passes the cookies back.
/// </summary>
public interface ICookieSetupProvider : IPlugin
{
    /// <summary>URL to navigate to for completing the challenge.</summary>
    string SetupUrl { get; }

    /// <summary>Cookie domain to extract cookies from (e.g. "db.bepis.moe").</summary>
    string CookieDomain { get; }

    /// <summary>Page title substring that indicates the challenge is completed.</summary>
    string CompletionTitleHint { get; }

    /// <summary>True when the plugin needs cookies before it can fetch data.</summary>
    bool NeedsCookieSetup { get; }

    /// <summary>
    /// Called by the host after the user completes the browser challenge.
    /// The plugin should persist these for future use.
    /// </summary>
    void ApplyCookies(IReadOnlyDictionary<string, string> cookies, string userAgent);
}

/// <summary>
/// Optional extension for cookie-backed plugins that can cheaply verify whether
/// their stored cookies still pass the provider's gate before a long import run.
/// </summary>
public interface ICookieSetupValidator : ICookieSetupProvider
{
    Task<bool> HasUsableCookiesAsync(CancellationToken ct);
}
