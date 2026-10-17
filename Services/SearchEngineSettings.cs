using Windows.Storage;

namespace CapyBara.Services;

public sealed record SearchEngineSettings(
    string Id,
    string Name,
    string SearchUrlTemplate,
    string? SuggestionUrlTemplate,
    string IconUrl)
{
    private const string SelectedEngineKey = "SelectedSearchEngine";

    public static IReadOnlyList<SearchEngineSettings> AvailableEngines { get; } =
    [
        new("google", "Google", "https://www.google.com/search?q={searchTerms}",
            "https://suggestqueries.google.com/complete/search?client=firefox&q={searchTerms}",
            "https://www.google.com/favicon.ico"),
        new("bing", "Bing", "https://www.bing.com/search?q={searchTerms}", null,
            "https://www.bing.com/sa/simg/favicon-trans-bg-blue-mg.ico"),
        new("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={searchTerms}",
            "https://duckduckgo.com/ac/?q={searchTerms}&type=list",
            "https://duckduckgo.com/favicon.ico")
    ];

    public static SearchEngineSettings Default => AvailableEngines[0];

    public static SearchEngineSettings Current
    {
        get
        {
            try
            {
                string? selectedId = ApplicationData.Current.LocalSettings.Values[SelectedEngineKey] as string;
                return AvailableEngines.FirstOrDefault(engine =>
                    string.Equals(engine.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? Default;
            }
            catch (Exception)
            {
                return Default;
            }
        }
    }

    public static SearchEngineSettings Select(string? id)
    {
        SearchEngineSettings selected = AvailableEngines.FirstOrDefault(engine =>
            string.Equals(engine.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

        try
        {
            ApplicationData.Current.LocalSettings.Values[SelectedEngineKey] = selected.Id;
        }
        catch (Exception)
        {
            // Settings remain usable for this session if persistent storage is unavailable.
        }

        return selected;
    }
}
