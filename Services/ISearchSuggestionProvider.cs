using CapyBara.Models;

namespace CapyBara.Services;

public interface ISearchSuggestionProvider
{
    Task<IReadOnlyList<SearchSuggestion>> GetSuggestionsAsync(
        SearchEngineSettings engine,
        string query,
        CancellationToken cancellationToken = default);
}
