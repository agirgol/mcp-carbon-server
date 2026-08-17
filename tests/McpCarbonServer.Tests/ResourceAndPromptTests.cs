using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Guards the non-tool half of the surface.
/// </summary>
/// <remarks>
/// Resources here are projected from the compiled catalog rather than written out, so these
/// assertions are about the projection staying wired up - a resource that silently stopped
/// reflecting the catalog would still return well-formed JSON.
/// </remarks>
[Collection(nameof(ServerUnderTest))]
public sealed class ResourceAndPromptTests(McpServerFixture fixture)
{
    [Fact]
    public async Task Catalog_index_is_offered_as_a_resource()
    {
        IList<McpClientResource> resources = await fixture.Client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(resources, resource => resource.Uri == "carbon://factor-sets");
    }

    [Fact]
    public async Task Per_set_and_per_report_resources_are_templated()
    {
        IList<McpClientResourceTemplate> templates = await fixture.Client.ListResourceTemplatesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        string[] uris = [.. templates.Select(template => template.UriTemplate)];

        Assert.Contains("carbon://factor-sets/{setId}", uris);
        Assert.Contains("carbon://gwp/{gwpSet}", uris);
    }

    [Fact]
    public async Task Catalog_index_resource_agrees_with_the_tool()
    {
        JsonElement fromTool = await fixture.CallAsync("list_factor_sets");
        JsonElement fromResource = await ReadJsonAsync("carbon://factor-sets");

        // Same projection behind both. If they ever disagree, one of the two paths has been
        // changed without the other and a client would get different answers depending on
        // how it asked.
        string[] toolIds = [.. fromTool.EnumerateArray().Select(set => set.GetProperty("id").GetString()!)];
        string[] resourceIds = [.. fromResource.EnumerateArray().Select(set => set.GetProperty("id").GetString()!)];

        Assert.Equal(toolIds, resourceIds);
    }

    [Fact]
    public async Task Gwp_resource_reports_the_potentials_actually_compiled_in()
    {
        JsonElement table = await ReadJsonAsync("carbon://gwp/Ar6");

        Assert.Equal("Ar6", table.GetProperty("set").GetString());
        Assert.Equal(100, table.GetProperty("timeHorizonYears").GetInt32());
        Assert.Equal("Verified", table.GetProperty("verification").GetString());
        Assert.NotEmpty(table.GetProperty("values").EnumerateArray());

        // Every gas the table publishes has to carry a potential; a zero here would silently
        // drop that gas out of every CO2e total computed under this report.
        foreach (JsonElement value in table.GetProperty("values").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("gas").GetString()));
            Assert.True(value.GetProperty("gwp").GetDouble() > 0);
        }
    }

    [Fact]
    public async Task Unknown_factor_set_resource_names_where_to_look()
    {
        Exception error = await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Client.ReadResourceAsync(
                "carbon://factor-sets/no-such-set",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("carbon://factor-sets", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_prompts_are_offered_with_their_arguments()
    {
        IList<McpClientPrompt> prompts = await fixture.Client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        McpClientPrompt intake = Assert.Single(prompts, prompt => prompt.Name == "ghg_inventory_intake");
        McpClientPrompt review = Assert.Single(prompts, prompt => prompt.Name == "disclosure_review");

        string[] intakeArguments = [.. intake.ProtocolPrompt.Arguments?.Select(argument => argument.Name) ?? []];

        Assert.Contains("organisation", intakeArguments);
        Assert.Contains("reportingYear", intakeArguments);
        Assert.False(string.IsNullOrWhiteSpace(review.Description));
    }

    [Fact]
    public async Task Intake_prompt_substitutes_its_arguments()
    {
        GetPromptResult result = await fixture.Client.GetPromptAsync(
            "ghg_inventory_intake",
            new Dictionary<string, object?>
            {
                ["organisation"] = "Acme A.Ş.",
                ["reportingYear"] = 2026,
                ["region"] = "Türkiye",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        PromptMessage message = Assert.Single(result.Messages);
        string text = Assert.IsType<TextContentBlock>(message.Content).Text;

        Assert.Equal(Role.User, message.Role);
        Assert.Contains("Acme A.Ş.", text, StringComparison.Ordinal);
        Assert.Contains("2026", text, StringComparison.Ordinal);
        Assert.Contains("Türkiye", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intake_prompt_omits_the_region_clause_when_none_is_given()
    {
        GetPromptResult result = await fixture.Client.GetPromptAsync(
            "ghg_inventory_intake",
            new Dictionary<string, object?>
            {
                ["organisation"] = "Acme",
                ["reportingYear"] = 2026,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;

        // The optional argument has to disappear cleanly rather than leaving "operating in"
        // dangling with nothing after it.
        Assert.DoesNotContain("operating in", text, StringComparison.Ordinal);
        Assert.Contains("Acme", text, StringComparison.Ordinal);
    }

    private async Task<JsonElement> ReadJsonAsync(string uri)
    {
        ReadResourceResult result = await fixture.Client.ReadResourceAsync(
            uri,
            cancellationToken: TestContext.Current.CancellationToken);

        TextResourceContents contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));

        return JsonSerializer.Deserialize<JsonElement>(contents.Text);
    }
}
