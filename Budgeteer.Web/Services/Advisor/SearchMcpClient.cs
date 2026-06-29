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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;
    private IReadOnlyList<AITool> _tools = Array.Empty<AITool>();
    private bool _attempted;

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
        var sibling = AppContext.BaseDirectory
            .Replace("Budgeteer.Web", "Budgeteer.SearchMcp");
        var dll = Path.Combine(sibling, "Budgeteer.SearchMcp.dll");
        return File.Exists(dll) ? dll : null;
    }

    /// <summary>
    /// Returns the search tools, connecting to the MCP server on first call. Always returns a list
    /// (empty if the server is unavailable) — never throws.
    /// </summary>
    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_attempted)
            return _tools;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_attempted)
                return _tools;
            _attempted = true;

            var serverDll = ResolveServerDll();
            if (serverDll is null)
            {
                _log.LogWarning("MCP search server assembly not found; web search is disabled. " +
                    "Set SearchMcp:ServerPath to enable it.");
                return _tools;
            }

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "budgeteer-search",
                Command = "dotnet",
                Arguments = [serverDll],
            });

            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
            _tools = tools.Cast<AITool>().ToList();
            _log.LogInformation("Connected to MCP search server; {Count} tool(s) available.", _tools.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not start the MCP search server; web search is disabled.");
        }
        finally
        {
            _gate.Release();
        }

        return _tools;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _gate.Dispose();
    }
}
