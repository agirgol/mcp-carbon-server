using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace McpCarbonServer;

/// <summary>
/// Entry point. Hosts the MCP server over stdio.
/// </summary>
internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        // Under the stdio transport, stdout is the JSON-RPC frame channel and nothing
        // else may write to it. A single log line on stdout desynchronises the framing,
        // and the host's failure mode is to drop the server silently rather than report
        // a parse error - so this is configured before anything else can log.
        //
        // standardErrorFromLevel: Verbose routes *every* level to stderr, not just
        // errors. ClearProviders() removes the console logger the generic host installs
        // by default, which writes to stdout and would otherwise reintroduce the
        // problem regardless of how Serilog is configured.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            await builder.Build().RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "mcp-carbon-server terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }
}
