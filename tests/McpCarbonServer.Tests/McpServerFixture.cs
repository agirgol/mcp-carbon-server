using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Starts the built server as a child process and connects an MCP client to it over stdio.
/// One process is shared by every test in the collection; the tools are pure functions over
/// a compiled-in catalog, so there is no state for one test to leak into another.
/// </summary>
public sealed class McpServerFixture : IAsyncLifetime
{
    private McpClient? _client;

    /// <summary>The connected client. Valid once <see cref="InitializeAsync"/> has run.</summary>
    public McpClient Client =>
        _client ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        string serverPath = ResolveServerAssemblyPath();

        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Name = "mcp-carbon-server",
            Command = "dotnet",
            Arguments = [serverPath],
        });

        _client = await McpClient.CreateAsync(
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    /// <summary>
    /// Calls a tool and returns the text payload, asserting the call did not fail.
    /// </summary>
    public async Task<JsonElement> CallAsync(string tool, object? arguments = null)
    {
        CallToolResult result = await CallRawAsync(tool, arguments);

        Assert.False(
            result.IsError is true,
            $"'{tool}' returned an error: {TextOf(result)}");

        return JsonSerializer.Deserialize<JsonElement>(TextOf(result));
    }

    /// <summary>
    /// Calls a tool without asserting success, for tests that are about the failure.
    /// </summary>
    public async Task<CallToolResult> CallRawAsync(string tool, object? arguments = null)
    {
        IReadOnlyDictionary<string, object?> args = arguments is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments))!;

        return await Client.CallToolAsync(
            tool,
            args,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Concatenates the text blocks of a tool result.</summary>
    public static string TextOf(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    private static string ResolveServerAssemblyPath()
    {
        string? configured = typeof(McpServerFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ServerAssemblyPath")
            ?.Value;

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "ServerAssemblyPath was not baked into the test assembly. Check the AssemblyMetadata item in the test project.");
        }

        string path = Path.GetFullPath(configured);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The server assembly was not found at '{path}'. Build the solution in this configuration first.");
        }

        return path;
    }
}

/// <summary>Shares one server process across every test class that opts into it.</summary>
[CollectionDefinition(nameof(ServerUnderTest))]
public sealed class ServerUnderTest : ICollectionFixture<McpServerFixture>;
