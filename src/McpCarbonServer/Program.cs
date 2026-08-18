using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Serilog;
using Serilog.Events;

namespace McpCarbonServer;

/// <summary>
/// Entry point. Serves MCP over stdio by default, or over HTTP with <c>--http</c>.
/// </summary>
internal static class Program
{
    private const string HttpSwitch = "--http";

    internal static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool http = args.Contains(HttpSwitch, StringComparer.Ordinal);

        // Removed before the host sees it. The command-line configuration provider expects
        // --key=value or --key value, so a bare flag would either swallow the argument after
        // it or be rejected outright.
        string[] hostArgs = [.. args.Where(argument => !string.Equals(argument, HttpSwitch, StringComparison.Ordinal))];

        try
        {
            return http
                ? await RunHttpAsync(hostArgs).ConfigureAwait(false)
                : await RunStdioAsync(hostArgs).ConfigureAwait(false);
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
    /// Serves one client over stdin and stdout, which is what a desktop MCP host launches.
    /// </summary>
    private static async Task<int> RunStdioAsync(string[] args)
    {
        // Under stdio, stdout is the JSON-RPC frame channel and nothing else may write to
        // it. A single log line there desynchronises the framing, and the host's failure
        // mode is to drop the server silently rather than report a parse error - so this is
        // configured before anything else can log.
        //
        // standardErrorFromLevel: Verbose routes every level to stderr, not just errors.
        ConfigureLogging(toStandardError: true);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // ClearProviders removes the console logger the generic host installs by default,
        // which writes to stdout and would reintroduce the problem regardless of how Serilog
        // is configured.
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        AddCarbonMcpServer(builder.Services).WithStdioServerTransport();

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Serves many clients over HTTP, which is what a deployment or a container runs.
    /// </summary>
    private static async Task<int> RunHttpAsync(string[] args)
    {
        // Nothing owns stdout here, and log collectors - containers especially - expect it
        // there. The stderr rule exists only because stdio takes stdout for the protocol.
        ConfigureLogging(toStandardError: false);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        // Streamable HTTP, and only that. The SDK marks the legacy SSE transport obsolete
        // because it has no request backpressure and is meant for completely trusted clients
        // in isolated processes; turning it on to widen client compatibility would trade a
        // real property of a network-facing server for reach it does not need. Transport
        // options are left at the SDK's defaults, which track the current protocol revision
        // - pinning them here would freeze decisions this server has no reason to make.
        AddCarbonMcpServer(builder.Services).WithHttpTransport();

        WebApplication app = builder.Build();

        app.MapMcp("/mcp");

        // Not part of MCP. A container orchestrator needs a cheap endpoint that answers
        // without opening a protocol session, and every MCP route expects a handshake.
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = ServerVersion }));

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Registers the server identity and every tool, resource and prompt in this assembly.
    /// </summary>
    /// <remarks>
    /// Shared by both transports on purpose. A capability that existed over one and not the
    /// other would be a difference nobody could explain from the outside.
    /// </remarks>
    private static IMcpServerBuilder AddCarbonMcpServer(IServiceCollection services) =>
        services
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
            .WithToolsFromAssembly(serializerOptions: SerializerOptions)
            // Resources take no serializer options and need none: they return JSON they
            // serialised themselves.
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly(serializerOptions: SerializerOptions);

    /// <summary>
    /// Serialisation for tool results, differing from the default in one respect: nulls are
    /// written rather than omitted.
    /// </summary>
    /// <remarks>
    /// The generated output schema marks every property required, including the nullable
    /// ones - a factor set with no end date, a scope 2 total under a method no line reported,
    /// an uncertainty the publisher never gave. Omitting those keys produces structured
    /// content that does not satisfy the schema the same server just published, and a client
    /// that validates one against the other rejects the result: the call fails with the tool
    /// having answered correctly, and nothing in the failure says why.
    ///
    /// Writing the null is also the better contract. "Not reported" and "absent from this
    /// response" are different claims, and a consumer should not have to tell them apart by
    /// checking whether a key exists.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(McpJsonUtilities.DefaultOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

    private static void ConfigureLogging(bool toStandardError)
    {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext();

        Log.Logger = toStandardError
            ? configuration.WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: LogTemplate).CreateLogger()
            : configuration.WriteTo.Console(outputTemplate: LogTemplate).CreateLogger();
    }

    private const string LogTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

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
