using System.Text.Json;
using System.Diagnostics;
using CapyBara.Models;

namespace CapyBara.Services;

public sealed class HttpSearchSuggestionProvider : ISearchSuggestionProvider
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<IReadOnlyList<SearchSuggestion>> GetSuggestionsAsync(
        SearchEngineSettings engine,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(engine.SuggestionUrlTemplate) ||
            string.IsNullOrWhiteSpace(query) || query.Length > 200)
        {
            return [];
        }

        string requestUrl = SearchNavigationService.ExpandTemplate(engine.SuggestionUrlTemplate, query);
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"Search suggestions from {engine.Name} returned HTTP {(int)response.StatusCode}.");
                return [];
            }
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            JsonElement items = json.RootElement;
            if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 1 &&
                items[1].ValueKind == JsonValueKind.Array)
            {
                items = items[1];
            }

            var suggestions = new List<SearchSuggestion>(8);
            if (items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    string? text = item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("phrase", out JsonElement phrase)
                            ? phrase.GetString()
                            : null;

                    if (!string.IsNullOrWhiteSpace(text) &&
                        !suggestions.Any(suggestion => string.Equals(suggestion.Text, text, StringComparison.OrdinalIgnoreCase)))
                    {
                        suggestions.Add(new SearchSuggestion(text));
                        if (suggestions.Count == 8)
                            break;
                    }
                }
            }

            return suggestions;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            Debug.WriteLine($"Search suggestions from {engine.Name} failed: {ex.Message}");
            return [];
        }
    }
}
