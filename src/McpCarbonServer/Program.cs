using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
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
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "mcp-carbon-server",
                        Title = "MCP Carbon Server",
                        Version = ServerVersion,
                        WebsiteUrl = "https://github.com/agirgol/mcp-carbon-server",
                    };

                    options.ServerInstructions = Instructions;
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly()
                .WithResourcesFromAssembly()
                .WithPromptsFromAssembly();

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

    /// <summary>
    /// How the server expects to be used, sent to the client at initialize.
    /// </summary>
    /// <remarks>
    /// The failure this is written against is a model answering a footprint question from a
    /// half-remembered factor instead of calling a tool, then presenting the number with no
    /// dataset behind it. Saying plainly that factors are looked up rather than recalled,
    /// and that the provenance is part of the answer, costs a few dozen tokens once per
    /// session.
    /// </remarks>
    private const string Instructions =
        "Greenhouse gas accounting over a compiled, source-cited emission factor catalog. " +
        "Emission factors are looked up here, never recalled from memory: start with " +
        "search_emission_factors or list_factor_sets to obtain a factor id, then pass that id " +
        "to calculate_emissions or build_inventory. Activity data may be supplied in any unit " +
        "measuring the same physical quantity as the factor's denominator and will be " +
        "converted; a unit from another dimension is rejected rather than guessed at. " +
        "Every result carries the dataset it came from, its publication year and whether that " +
        "dataset has been verified against its cited source - report those alongside any " +
        "figure, and treat a figure from an unverified set as unfit for a disclosure.";

    /// <summary>
    /// The package version, as opposed to the four-part assembly version.
    /// </summary>
    /// <remarks>
    /// The assembly version of a 0.1.0-alpha.1 build is 0.1.0.0, which tells a client
    /// nothing about whether it is talking to a pre-release. The informational version
    /// carries the real one; the suffix after '+' is the source-link commit hash, which is
    /// noise in a client's server list.
    /// </remarks>
    private static string ServerVersion
    {
        get
        {
            string? informational = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            return string.IsNullOrWhiteSpace(informational)
                ? "0.0.0"
                : informational.Split('+')[0];
        }
    }
}
