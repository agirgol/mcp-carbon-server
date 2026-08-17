using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
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

    private async Task<McpClientTool> GetToolAsync(string name)
    {
        IList<McpClientTool> tools = await fixture.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(tools, tool => tool.Name == name);
    }
}
