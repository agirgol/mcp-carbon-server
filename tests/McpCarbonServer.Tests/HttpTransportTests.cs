using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Guards the HTTP transport, and the promise that it serves the same server.
/// </summary>
[Collection(nameof(ServerUnderTest))]
public sealed class HttpTransportTests(McpServerFixture stdio, HttpServerFixture http)
{
    [Fact]
    public async Task Health_endpoint_answers_without_a_protocol_session()
    {
        // Every MCP route expects a handshake first. An orchestrator restarting a container
        // needs something cheaper than that, which is the only reason this endpoint exists.
        using HttpResponseMessage response = await http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Legacy_sse_endpoints_are_not_mapped()
    {
        // The SDK marks legacy SSE obsolete: no request backpressure, trusted clients in
        // isolated processes only. Leaving it off is a decision, so it is pinned rather than
        // left to whatever a future default does.
        using HttpResponseMessage response = await http.GetAsync("/sse");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Both_transports_expose_the_same_tools()
    {
        IList<McpClientTool> overStdio = await stdio.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientTool> overHttp = await http.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // The registration is shared by construction today. This is what stops that being
        // quietly undone: a capability available over one transport and not the other is a
        // difference nobody could explain from the outside.
        Assert.Equal(
            overStdio.Select(tool => tool.Name).Order(),
            overHttp.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task Both_transports_expose_the_same_resources_and_prompts()
    {
        IList<McpClientResource> stdioResources = await stdio.Client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientResource> httpResources = await http.Client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        IList<McpClientPrompt> stdioPrompts = await stdio.Client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientPrompt> httpPrompts = await http.Client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            stdioResources.Select(resource => resource.Uri).Order(),
            httpResources.Select(resource => resource.Uri).Order());
        Assert.Equal(
            stdioPrompts.Select(prompt => prompt.Name).Order(),
            httpPrompts.Select(prompt => prompt.Name).Order());
    }

    [Fact]
    public async Task Both_transports_identify_the_server_the_same_way()
    {
        Assert.Equal(stdio.Client.ServerInfo.Name, http.Client.ServerInfo.Name);
        Assert.Equal(stdio.Client.ServerInfo.Version, http.Client.ServerInfo.Version);
        Assert.Equal(stdio.Client.ServerInstructions, http.Client.ServerInstructions);
    }

    [Fact]
    public async Task A_tool_call_over_http_returns_the_same_answer()
    {
        Dictionary<string, object?> arguments = new()
        {
            ["value"] = 1.0,
            ["fromUnit"] = "CubicMetre",
            ["toUnit"] = "Litre",
        };

        CallToolResult overStdio = await stdio.Client.CallToolAsync(
            "convert_units", arguments, cancellationToken: TestContext.Current.CancellationToken);
        CallToolResult overHttp = await http.Client.CallToolAsync(
            "convert_units", arguments, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(overStdio.StructuredContent);
        Assert.NotNull(overHttp.StructuredContent);
        Assert.Equal(
            overStdio.StructuredContent!.Value.GetProperty("value").GetDouble(),
            overHttp.StructuredContent!.Value.GetProperty("value").GetDouble());
    }
}
