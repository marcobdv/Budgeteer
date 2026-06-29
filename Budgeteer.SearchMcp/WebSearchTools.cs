using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace Budgeteer.SearchMcp;

/// <summary>
/// MCP tools for researching information on the web. Backed by the Tavily Search API
/// (https://tavily.com), which is built for LLM use and has a free tier. The key is read from
/// <c>TAVILY_API_KEY</c> (or config <c>Tavily:ApiKey</c>); with no key the tool returns a clear
/// "not configured" message rather than failing, so the server stays runnable out of the box.
///
/// To use a different search provider, swap the implementation of <see cref="WebSearch"/> — the
/// MCP contract (a query in, a text summary out) stays the same.
/// </summary>
[McpServerToolType]
public sealed class WebSearchTools
{
    private const string TavilyEndpoint = "https://api.tavily.com/search";

    // One shared client for the process — avoids socket exhaustion across tool calls.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly IConfiguration _config;

    public WebSearchTools(IConfiguration config) => _config = config;

    [McpServerTool(Name = "web_search")]
    [Description("Search the web for current information and return the top results as titles, " +
        "URLs, and snippets. Use this to research real-world options the user's own data can't " +
        "answer — for example cheaper alternatives to a subscription or bill, current prices, " +
        "or provider comparisons. Always pass a specific query (include locale, e.g. the country, " +
        "when relevant).")]
    public async Task<string> WebSearch(
        [Description("The search query. Be specific; include country/locale and the year when relevant.")]
        string query,
        [Description("Maximum number of results to return (1-10). Defaults to 5.")]
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "No query was provided.";

        var apiKey = _config["Tavily:ApiKey"] ?? Environment.GetEnvironmentVariable("TAVILY_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "Web search is not configured. Set the TAVILY_API_KEY environment variable " +
                   "(get a free key at https://tavily.com) to enable web research.";

        maxResults = Math.Clamp(maxResults, 1, 10);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TavilyEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                query,
                max_results = maxResults,
                search_depth = "basic",
                include_answer = true,
            });

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return $"Web search failed ({(int)response.StatusCode}): {Truncate(body, 300)}";
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            var sb = new StringBuilder();
            sb.AppendLine($"Web search results for: {query}");

            if (root.TryGetProperty("answer", out var answer) &&
                answer.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(answer.GetString()))
            {
                sb.AppendLine();
                sb.AppendLine($"Summary: {answer.GetString()}");
            }

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                int i = 1;
                foreach (var r in results.EnumerateArray())
                {
                    var title = GetString(r, "title");
                    var url = GetString(r, "url");
                    var content = GetString(r, "content");
                    sb.AppendLine();
                    sb.AppendLine($"{i}. {title}");
                    sb.AppendLine($"   {url}");
                    if (!string.IsNullOrWhiteSpace(content))
                        sb.AppendLine($"   {Truncate(content, 400)}");
                    i++;
                }
                if (i == 1)
                    sb.AppendLine("(No results found.)");
            }

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            return "Web search timed out. Try again or narrow the query.";
        }
        catch (Exception ex)
        {
            return $"Web search error: {ex.Message}";
        }
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
