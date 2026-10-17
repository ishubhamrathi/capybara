using CapyBara.Models;

namespace CapyBara.Services;

public sealed class SearchSuggestionService(ISearchSuggestionProvider provider)
{
    public Task<IReadOnlyList<SearchSuggestion>> GetSuggestionsAsync(
        string? engineId,
        string query,
        CancellationToken cancellationToken = default)
    {
        SearchEngineSettings engine = SearchEngineSettings.AvailableEngines.FirstOrDefault(item =>
            string.Equals(item.Id, engineId, StringComparison.OrdinalIgnoreCase)) ?? SearchEngineSettings.Current;
        return provider.GetSuggestionsAsync(engine, query, cancellationToken);
    }
}
