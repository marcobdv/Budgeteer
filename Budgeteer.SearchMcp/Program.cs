using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A small Model Context Protocol (MCP) server that exposes a single web-search tool.
// It speaks MCP over stdio, so a host process (here, Budgeteer.Web's advisor agent) launches
// it as a subprocess and calls its tools. stdout is the protocol channel, so ALL logging must
// go to stderr — otherwise log lines would corrupt the MCP message stream.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
