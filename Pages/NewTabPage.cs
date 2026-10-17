using Microsoft.Web.WebView2.Core;

namespace CapyBara.Pages;

public static class NewTabPage
{
    public const string HostName = "capybara.test";
    public const string Source = "https://capybara.test/Pages/NewTabPage.html";

    public static void Configure(CoreWebView2 core)
    {
        core.SetVirtualHostNameToFolderMapping(
            HostName,
            AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
    }

    public static bool IsSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
        string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.AbsolutePath, "/Pages/NewTabPage.html", StringComparison.OrdinalIgnoreCase);
}
