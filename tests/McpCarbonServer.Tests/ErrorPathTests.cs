using System;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Guards that a mistake the caller can fix comes back with the information needed to fix
/// it.
/// </summary>
/// <remarks>
/// The server reports an unhandled exception to the client as a bare "an error occurred",
/// deliberately withholding the message so internal detail cannot leak. Only the message on
/// an <c>McpException</c> travels. Changing a throw from one to the other is a one-word edit
/// that silently strips the guidance and breaks nothing the compiler can see, which is what
/// these tests exist to catch.
/// </remarks>
[Collection(nameof(ServerUnderTest))]
public sealed class ErrorPathTests(McpServerFixture fixture)
{
    [Fact]
    public async Task Unknown_factor_id_points_at_the_search_tool()
    {
        CallToolResult result = await fixture.CallRawAsync(
            "calculate_emissions",
            new { value = 10.0, unit = "KilowattHour", factorId = "no-such-factor" });

        Assert.True(result.IsError);

        string message = McpServerFixture.TextOf(result);
        Assert.Contains("no-such-factor", message);
        Assert.Contains("search_emission_factors", message);
    }

    [Fact]
    public async Task Unit_from_another_dimension_names_both_dimensions()
    {
        string factorId = await CalculationWiringTests.FindFactorAsync(fixture, "CubicMetre");

        CallToolResult result = await fixture.CallRawAsync(
            "calculate_emissions",
            new { value = 10.0, unit = "KilowattHour", factorId });

        Assert.True(result.IsError);

        // Naming both quantities is the difference between an error the caller can act on
        // and one they can only retry. The library refuses to convert across dimensions
        // rather than assume a calorific value, so the fix is a different unit or a
        // different factor - and the message has to say which.
        string message = McpServerFixture.TextOf(result);
        Assert.Contains("Volume", message);
        Assert.Contains("Energy", message);
    }

    [Fact]
    public async Task Aggregate_only_factor_names_the_assessment_report_that_works()
    {
        // Many published tables give a single CO2e figure and no per-gas breakdown -
        // most of DEFRA's material-use and waste rows are like this. Such a figure is
        // only meaningful under the report it was aggregated with, and the library
        // refuses to re-aggregate it rather than invent the split the publisher withheld.
        //
        // That refusal is correct and recoverable: the same factor works under its own
        // set. Whether the caller can discover that depends entirely on the message
        // surviving, which is what this asserts.
        (string factorId, string workingSet) = await FindAggregateOnlyFactorAsync(fixture);

        string otherSet = workingSet == "Ar6" ? "Ar5" : "Ar6";

        CallToolResult result = await fixture.CallRawAsync(
            "calculate_emissions",
            new { value = 10.0, unit = "Tonne", factorId, gwpSet = otherSet });

        Assert.True(result.IsError);

        string message = McpServerFixture.TextOf(result);
        Assert.Contains(factorId, message, StringComparison.Ordinal);
        Assert.Contains(workingSet, message, StringComparison.Ordinal);
        Assert.Contains("gwpSet", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds a factor that publishes only an aggregate CO2e figure, along with the
    /// assessment report it was published under, by trying each set until one works.
    /// </summary>
    /// <remarks>
    /// Discovered rather than hard-coded: which rows are aggregate-only is a property of
    /// whichever catalog is compiled in, and a pinned id would retire with the next
    /// dataset revision.
    /// </remarks>
    private static async Task<(string FactorId, string WorkingSet)> FindAggregateOnlyFactorAsync(
        McpServerFixture fixture)
    {
        JsonElement search = await fixture.CallAsync(
            "search_emission_factors",
            new { scope = "Scope3", limit = 200 });

        foreach (JsonElement factor in search.GetProperty("factors").EnumerateArray())
        {
            if (factor.GetProperty("unit").GetString() != "Tonne")
            {
                continue;
            }

            string id = factor.GetProperty("id").GetString()!;

            // A factor with a gas breakdown succeeds under every set and is not the
            // subject here; one that succeeds under exactly one is.
            string? working = null;
            int successes = 0;

            foreach (string set in new[] { "Ar4", "Ar5", "Ar6" })
            {
                CallToolResult attempt = await fixture.CallRawAsync(
                    "calculate_emissions",
                    new { value = 10.0, unit = "Tonne", factorId = id, gwpSet = set });

                // IsError is nullable; absent means success.
                if (attempt.IsError != true)
                {
                    successes++;
                    working ??= set;
                }
            }

            if (successes == 1 && working is not null)
            {
                return (id, working);
            }
        }

        throw new InvalidOperationException(
            "No aggregate-only scope 3 factor is compiled into this build, so the guard has nothing to check.");
    }

    [Fact]
    public async Task Market_based_total_over_location_based_lines_explains_itself()
    {
        // The inventory has to actually report scope 2 for this to be a question. With no
        // scope 2 line at all nothing is missing from the total, and the library correctly
        // does not object - so the guard only has something to say once purchased energy is
        // present under one method and asked for under the other.
        string factorId = await CalculationWiringTests.FindScope2FactorAsync(fixture);

        CallToolResult result = await fixture.CallRawAsync(
            "build_inventory",
            new
            {
                lines = new[] { new { value = 1000.0, unit = "KilowattHour", factorId } },
                scope2Method = "MarketBased",
            });

        Assert.True(result.IsError);

        // A total that quietly omitted scope 2 would be worse than no total at all.
        string message = McpServerFixture.TextOf(result);
        Assert.Contains("MarketBased", message);
    }

    [Fact]
    public async Task Inventory_without_scope_2_totals_under_either_method()
    {
        // The complement of the test above, kept next to it so the boundary is documented
        // rather than inferred: asking for a method nothing reports is only an error when
        // there is scope 2 to report.
        string factorId = await CalculationWiringTests.FindFactorAsync(fixture, "CubicMetre");

        CallToolResult result = await fixture.CallRawAsync(
            "build_inventory",
            new
            {
                lines = new[] { new { value = 100.0, unit = "CubicMetre", factorId } },
                scope2Method = "MarketBased",
            });

        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public async Task Empty_inventory_is_rejected()
    {
        CallToolResult result = await fixture.CallRawAsync(
            "build_inventory",
            new { lines = System.Array.Empty<object>() });

        Assert.True(result.IsError);
        Assert.Contains("at least one", McpServerFixture.TextOf(result), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Impossible_unit_conversion_is_rejected()
    {
        CallToolResult result = await fixture.CallRawAsync(
            "convert_units",
            new { value = 1.0, fromUnit = "KilowattHour", toUnit = "Litre" });

        Assert.True(result.IsError);
    }
}
