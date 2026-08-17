using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Guards the shape of the tool surface. A parameter renamed or a tool dropped is invisible
/// to the compiler - the client only finds out at run time, by which point it is a broken
/// integration rather than a failed build.
/// </summary>
[Collection(nameof(ServerUnderTest))]
public sealed class ToolContractTests(McpServerFixture fixture)
{
    private static readonly string[] ExpectedTools =
    [
        "list_factor_sets",
        "search_emission_factors",
        "calculate_emissions",
        "build_inventory",
        "convert_units",
    ];

    [Fact]
    public async Task Server_exposes_exactly_the_documented_tools()
    {
        IList<McpClientTool> tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ExpectedTools.Order(),
            tools.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task Every_tool_carries_a_description()
    {
        IList<McpClientTool> tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The description is the only thing a model has to decide whether a tool applies.
        // An undescribed tool is one the model will either ignore or misuse.
        foreach (McpClientTool tool in tools)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(tool.Description),
                $"'{tool.Name}' has no description.");
        }
    }

    [Theory]
    [InlineData("calculate_emissions", new[] { "value", "unit", "factorId" })]
    [InlineData("convert_units", new[] { "value", "fromUnit", "toUnit" })]
    [InlineData("build_inventory", new[] { "lines" })]
    public async Task Tool_requires_its_mandatory_parameters(string toolName, string[] required)
    {
        McpClientTool tool = await GetToolAsync(toolName);

        JsonElement schema = tool.JsonSchema;
        HashSet<string> declaredRequired = schema.TryGetProperty("required", out JsonElement requiredElement)
            ? [.. requiredElement.EnumerateArray().Select(item => item.GetString()!)]
            : [];

        JsonElement properties = schema.GetProperty("properties");

        foreach (string parameter in required)
        {
            Assert.True(
                properties.TryGetProperty(parameter, out _),
                $"'{toolName}' does not declare a '{parameter}' parameter.");
            Assert.Contains(parameter, declaredRequired);
        }
    }

    [Fact]
    public async Task Optional_parameters_are_not_marked_required()
    {
        McpClientTool tool = await GetToolAsync("calculate_emissions");

        JsonElement schema = tool.JsonSchema;
        HashSet<string> declaredRequired = schema.TryGetProperty("required", out JsonElement requiredElement)
            ? [.. requiredElement.EnumerateArray().Select(item => item.GetString()!)]
            : [];

        // gwpSet defaults to AR6. Marking it required would force every caller to choose an
        // assessment report, which is exactly the decision most callers should not have to
        // make.
        Assert.DoesNotContain("gwpSet", declaredRequired);
    }

    [Fact]
    public async Task Enum_parameters_publish_their_allowed_values()
    {
        McpClientTool tool = await GetToolAsync("convert_units");

        JsonElement unit = tool.JsonSchema.GetProperty("properties").GetProperty("fromUnit");

        // Units are an enum in the schema rather than a free string, so the model picks from
        // the list instead of inventing 'kwh' and getting a validation error back.
        Assert.True(unit.TryGetProperty("enum", out JsonElement values), "fromUnit is not constrained to an enum.");
        Assert.Contains("KilowattHour", values.EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Every_tool_declares_an_output_schema()
    {
        IList<McpClientTool> tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Without an output schema a result is an opaque string the client has to parse and
        // hope about. With one it is data the client can validate against a contract, and
        // the shape is part of what a breaking change would have to break.
        foreach (McpClientTool tool in tools)
        {
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
        }
    }

    [Fact]
    public async Task Every_tool_is_annotated_as_a_safe_read()
    {
        IList<McpClientTool> tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Every tool here is a pure function over a catalog compiled into the binary: it
        // reads nothing outside the process and writes nothing at all. Saying so lets a
        // client call them without an approval prompt. Left unset, a client has to assume
        // the opposite.
        foreach (McpClientTool tool in tools)
        {
            ToolAnnotations? annotations = tool.ProtocolTool.Annotations;

            Assert.NotNull(annotations);
            Assert.True(annotations!.ReadOnlyHint, $"'{tool.Name}' is not marked read-only.");
            Assert.True(annotations.IdempotentHint, $"'{tool.Name}' is not marked idempotent.");
            Assert.False(annotations.OpenWorldHint, $"'{tool.Name}' is marked open-world.");
            Assert.False(string.IsNullOrWhiteSpace(annotations.Title), $"'{tool.Name}' has no title.");
        }
    }

    [Fact]
    public async Task Results_carry_structured_content()
    {
        CallToolResult result = await fixture.CallRawAsync(
            "convert_units",
            new { value = 1.0, fromUnit = "CubicMetre", toUnit = "Litre" });

        Assert.NotNull(result.StructuredContent);

        JsonElement structured = result.StructuredContent!.Value;
        Assert.Equal(1000.0, structured.GetProperty("value").GetDouble());
        Assert.Equal("Volume", structured.GetProperty("dimension").GetString());
    }

    [Fact]
    public void Server_reports_its_package_version_and_instructions()
    {
        // The four-part assembly version of a pre-release build is 0.1.0.0, which cannot tell
        // a client whether it is talking to an alpha. The package version can.
        Assert.NotNull(fixture.Client.ServerInfo);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Client.ServerInfo.Version));
        Assert.NotEqual("0.0.0", fixture.Client.ServerInfo.Version);

        Assert.False(string.IsNullOrWhiteSpace(fixture.Client.ServerInstructions));
    }

    private async Task<McpClientTool> GetToolAsync(string name)
    {
        IList<McpClientTool> tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(tools, tool => tool.Name == name);
    }
}
