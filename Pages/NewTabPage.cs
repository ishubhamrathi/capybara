using System.Reflection;

namespace CapyBara.Pages;

public static class NewTabPage
{
    private static readonly Lazy<string> PageHtml = new(LoadHtml);

    public static string Html => PageHtml.Value;

    private static string LoadHtml()
    {
        const string resourceName = "CapyBara.Pages.NewTabPage.html";
        using Stream stream = typeof(NewTabPage).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        string html = reader.ReadToEnd();

        const string logoResourceName = "CapyBara.Assets.CapyBaraNewTabLogo.png";
        using Stream logoStream = typeof(NewTabPage).Assembly.GetManifestResourceStream(logoResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{logoResourceName}' was not found.");
        using var logoBuffer = new MemoryStream();
        logoStream.CopyTo(logoBuffer);
        string logoDataUri = $"data:image/png;base64,{Convert.ToBase64String(logoBuffer.ToArray())}";

        return html.Replace("{{CAPYBARA_LOGO_DATA_URI}}", logoDataUri, StringComparison.Ordinal);
    }
}
