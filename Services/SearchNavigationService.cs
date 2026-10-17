namespace CapyBara.Services;

public sealed class SearchNavigationService
{
    public string CreateSearchUrl(string query, string? engineId = null)
    {
        SearchEngineSettings engine = SearchEngineSettings.AvailableEngines.FirstOrDefault(item =>
            string.Equals(item.Id, engineId, StringComparison.OrdinalIgnoreCase)) ?? SearchEngineSettings.Current;
        return ExpandTemplate(engine.SearchUrlTemplate, query);
    }

    internal static string ExpandTemplate(string template, string query) =>
        template.Replace("{searchTerms}", Uri.EscapeDataString(query), StringComparison.Ordinal);
}
