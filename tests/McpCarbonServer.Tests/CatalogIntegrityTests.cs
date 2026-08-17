using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Guards the catalog the server actually resolves against.
/// </summary>
[Collection(nameof(ServerUnderTest))]
public sealed class CatalogIntegrityTests(McpServerFixture fixture)
{
    [Fact]
    public async Task Catalog_is_not_empty()
    {
        JsonElement sets = await fixture.CallAsync("list_factor_sets");

        Assert.NotEmpty(sets.EnumerateArray());
    }

    [Fact]
    public async Task Every_compiled_factor_set_is_verified()
    {
        JsonElement sets = await fixture.CallAsync("list_factor_sets");

        // This is the regression guard for the build wiring, not a statement about the
        // library. GhgAccounting defaults GhgRequireVerifiedCatalog to false, which compiles
        // in the synthetic sets under data/examples/; the release workflow packs with it on,
        // which drops them. Left alone, the server would resolve example factor ids against
        // a local project reference and throw against the published package - a difference
        // that only shows up in CI. The local reference forces the gate on, and a synthetic
        // set can never reach Verified, so this assertion fails the moment that slips.
        foreach (JsonElement set in sets.EnumerateArray())
        {
            Assert.Equal("Verified", set.GetProperty("verification").GetString());
        }
    }

    [Fact]
    public async Task Every_factor_set_cites_a_source()
    {
        JsonElement sets = await fixture.CallAsync("list_factor_sets");

        // A figure whose provenance the server cannot state is not usable in a disclosure,
        // so a set arriving without one is a defect rather than a gap to tolerate.
        foreach (JsonElement set in sets.EnumerateArray())
        {
            JsonElement source = set.GetProperty("source");

            Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("publisher").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("title").GetString()));
            Assert.True(source.GetProperty("publicationYear").GetInt32() > 2000);
        }
    }

    [Fact]
    public async Task Search_returns_factors_whose_set_is_listed()
    {
        JsonElement sets = await fixture.CallAsync("list_factor_sets");
        string[] setIds = [.. sets.EnumerateArray().Select(set => set.GetProperty("id").GetString()!)];

        JsonElement factors = await fixture.CallAsync(
            "search_emission_factors",
            new { query = "gas", limit = 20 });

        Assert.NotEmpty(factors.EnumerateArray());

        foreach (JsonElement factor in factors.EnumerateArray())
        {
            Assert.Contains(factor.GetProperty("setId").GetString(), setIds);
        }
    }

    [Fact]
    public async Task Search_honours_its_limit()
    {
        JsonElement factors = await fixture.CallAsync(
            "search_emission_factors",
            new { limit = 3 });

        Assert.Equal(3, factors.EnumerateArray().Count());
    }
}
