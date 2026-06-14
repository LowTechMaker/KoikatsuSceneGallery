using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

internal static class CookieSetupDialogService
{
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        DispatcherQueue dispatcherQueue,
        ICookieSetupProvider provider,
        string closeButtonText)
    {
        var webView = new WebView2 { MinWidth = 800, MinHeight = 600 };
        var completed = false;
        var isCompleting = false;
        CancellationTokenSource? completionPollingCts = null;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"{provider.Name} - Cloudflare Setup",
            Content = webView,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Close,
        };

        async Task<bool> TryApplyCookiesAsync()
        {
            if (webView.CoreWebView2 is null)
                return false;

            var cookies = await webView.CoreWebView2.CookieManager
                .GetCookiesAsync($"https://{provider.CookieDomain}");
            var cookieDict = cookies.ToDictionary(c => c.Name, c => c.Value);
            if (!cookieDict.ContainsKey("cf_clearance"))
                return false;

            var userAgent = await webView.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
            if (userAgent is not null)
                userAgent = JsonSerializer.Deserialize<string>(userAgent);

            provider.ApplyCookies(cookieDict, userAgent ?? "");
            return true;
        }

        async Task<bool> IsCompletionPageAsync()
        {
            if (webView.CoreWebView2 is null)
                return false;

            var titleJson = await webView.CoreWebView2.ExecuteScriptAsync("document.title");
            var title = JsonSerializer.Deserialize<string>(titleJson);
            return title is not null
                && title.Contains(provider.CompletionTitleHint, StringComparison.OrdinalIgnoreCase)
                && !title.Contains("challenge", StringComparison.OrdinalIgnoreCase)
                && !title.Contains("just a moment", StringComparison.OrdinalIgnoreCase);
        }

        async Task TryCompleteAsync()
        {
            if (isCompleting || completed)
                return;

            isCompleting = true;
            try
            {
                if (await IsCompletionPageAsync() && await TryApplyCookiesAsync())
                {
                    completed = true;
                    completionPollingCts?.Cancel();
                    dialog.Hide();
                }
            }
            catch
            {
            }
            finally
            {
                isCompleting = false;
            }
        }

        void StartCompletionPolling()
        {
            completionPollingCts?.Cancel();
            completionPollingCts = new CancellationTokenSource();
            var token = completionPollingCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && !completed)
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                        _ = dispatcherQueue.TryEnqueue(async () => await TryCompleteAsync());
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        dialog.Loaded += async (_, _) =>
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.Navigate(provider.SetupUrl);
                StartCompletionPolling();

                webView.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    if (args.IsSuccess)
                        await TryCompleteAsync();
                };
            }
            catch (Exception ex)
            {
                webView.Visibility = Visibility.Collapsed;
                dialog.Content = new TextBlock
                {
                    Text = $"Failed to initialize WebView2: {ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                };
            }
        };

        await dialog.ShowAsync();

        completionPollingCts?.Cancel();
        completionPollingCts?.Dispose();
        webView.Close();
        return completed && !provider.NeedsCookieSetup;
    }
}
