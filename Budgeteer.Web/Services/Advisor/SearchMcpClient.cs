using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Budgeteer.Web.Services.Advisor;

/// <summary>
/// Connects to the <c>Budgeteer.SearchMcp</c> MCP server (launched as a stdio subprocess) and
/// exposes its tools as <see cref="AITool"/>s for the advisor agent. Registered as a singleton so
/// a single server process is shared across all chat sessions; the connection is established lazily
/// on first use and cached.
///
/// Failure is non-fatal: if the server can't be found or started (e.g. it wasn't built, or
/// <c>dotnet</c> isn't on PATH), web search is simply unavailable and the advisor keeps working
/// with its data tools.
/// </summary>
public sealed class SearchMcpClient : IAsyncDisposable
{
    private readonly ILogger<SearchMcpClient> _log;
    private readonly IConfiguration _config;
    private readonly object _lock = new();
    private McpClient? _client;
    private Task<IReadOnlyList<AITool>>? _connectTask;

    public SearchMcpClient(ILogger<SearchMcpClient> log, IConfiguration config)
    {
        _log = log;
        _config = config;
    }

    /// <summary>
    /// Locates the built MCP server assembly. The server runs from its own output directory (which
    /// contains its dependencies and <c>.deps.json</c>), so we resolve a sibling-project path from
    /// this app's base directory. Override with config <c>SearchMcp:ServerPath</c> (e.g. when
    /// published) to point at a specific assembly.
    /// </summary>
    private string? ResolveServerDll()
    {
        var configured = _config["SearchMcp:ServerPath"];
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;

        // .../Budgeteer.Web/bin/<cfg>/net9.0  ->  .../Budgeteer.SearchMcp/bin/<cfg>/net9.0
        // Replace only the project directory segment: a blanket string.Replace would also
        // rewrite a parent folder that happens to contain "Budgeteer.Web" in its name.
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = baseDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var idx = Array.FindLastIndex(segments, s => string.Equals(s, "Budgeteer.Web", StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return null;
        segments[idx] = "Budgeteer.SearchMcp";
        var dll = Path.Combine(string.Join(Path.DirectorySeparatorChar, segments), "Budgeteer.SearchMcp.dll");
        return File.Exists(dll) ? dll : null;
    }

    /// <summary>
    /// Returns the search tools, connecting to the MCP server on first call. Always returns a list
    /// (empty if the server is unavailable) — never throws. Concurrent callers share one connect
    /// attempt and all wait for its result: an "attempted" flag set before the (seconds-long)
    /// connect would let a second circuit grab the still-empty tool list and cache an agent
    /// without web search for the life of that circuit.
    /// </summary>
    public Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _connectTask ??= ConnectAsync();
        }
        // The shared connect keeps running even if this caller gives up waiting.
        return _connectTask.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AITool>> ConnectAsync()
    {
        try
        {
            var serverDll = ResolveServerDll();
            if (serverDll is null)
            {
                _log.LogWarning("MCP search server assembly not found; web search is disabled. " +
                    "Set SearchMcp:ServerPath to enable it.");
                return Array.Empty<AITool>();
            }

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "budgeteer-search",
                Command = "dotnet",
                Arguments = [serverDll],
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            var tools = await _client.ListToolsAsync(cancellationToken: timeout.Token);
            var result = (IReadOnlyList<AITool>)tools.Cast<AITool>().ToList();
            _log.LogInformation("Connected to MCP search server; {Count} tool(s) available.", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not start the MCP search server; web search is disabled for now.");
            // Clear the cached attempt so a later circuit retries — a transient startup
            // failure shouldn't disable web search for the whole process lifetime.
            lock (_lock)
            {
                _connectTask = null;
            }
            return Array.Empty<AITool>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }
}
