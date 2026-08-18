using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Guards how the server wires activity data through a factor - not the arithmetic behind
/// the factor, which is GhgAccounting's own test suite's job.
/// </summary>
/// <remarks>
/// Every assertion here holds for any catalog. Nothing pins a factor id or an expected
/// kilogram figure, so next year's dataset does not turn this file red for a reason that has
/// nothing to do with the server.
/// </remarks>
[Collection(nameof(ServerUnderTest))]
public sealed class CalculationWiringTests(McpServerFixture fixture)
{
    [Fact]
    public async Task Total_equals_the_sum_of_its_gas_rows()
    {
        string factorId = await FindFactorAsync(fixture, "CubicMetre");

        JsonElement result = await fixture.CallAsync(
            "calculate_emissions",
            new { value = 1000.0, unit = "CubicMetre", factorId });

        double total = result.GetProperty("co2e").GetProperty("value").GetDouble();
        double summed = result.GetProperty("gases")
            .EnumerateArray()
            .Sum(gas => gas.GetProperty("co2e").GetProperty("value").GetDouble());

        Assert.True(total > 0, "A fuel factor produced no emissions.");
        AssertClose(total, summed);
    }

    [Fact]
    public async Task Emissions_are_reported_with_their_unit()
    {
        string factorId = await FindFactorAsync(fixture, "CubicMetre");

        JsonElement result = await fixture.CallAsync(
            "calculate_emissions",
            new { value = 1000.0, unit = "CubicMetre", factorId });

        // A bare number is how a wrong figure reaches a report. The unit travels with it.
        Assert.Equal("Kilogram", result.GetProperty("co2e").GetProperty("unit").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("verification").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("source").GetProperty("publisher").GetString()));
    }

    [Fact]
    public async Task Equivalent_activity_in_a_different_unit_gives_the_same_answer()
    {
        string factorId = await FindFactorAsync(fixture, "CubicMetre");

        JsonElement inCubicMetres = await fixture.CallAsync(
            "calculate_emissions",
            new { value = 1000.0, unit = "CubicMetre", factorId });

        JsonElement inLitres = await fixture.CallAsync(
            "calculate_emissions",
            new { value = 1_000_000.0, unit = "Litre", factorId });

        // Proves the conversion is applied rather than the magnitude being taken at face
        // value against a factor denominated in something else.
        AssertClose(
            inCubicMetres.GetProperty("co2e").GetProperty("value").GetDouble(),
            inLitres.GetProperty("co2e").GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task Inventory_totals_the_lines_it_is_given()
    {
        string factorId = await FindFactorAsync(fixture, "CubicMetre");

        JsonElement single = await fixture.CallAsync(
            "calculate_emissions",
            new { value = 500.0, unit = "CubicMetre", factorId });

        JsonElement inventory = await fixture.CallAsync(
            "build_inventory",
            new
            {
                lines = new[]
                {
                    new { value = 500.0, unit = "CubicMetre", factorId },
                    new { value = 500.0, unit = "CubicMetre", factorId },
                },
            });

        double lineTotal = single.GetProperty("co2e").GetProperty("value").GetDouble();

        Assert.Equal(2, inventory.GetProperty("lineCount").GetInt32());
        AssertClose(lineTotal * 2, inventory.GetProperty("total").GetProperty("value").GetDouble());
        AssertClose(lineTotal * 2, inventory.GetProperty("scope1").GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task Scope_2_is_reported_under_the_method_its_factors_carry()
    {
        string factorId = await FindScope2FactorAsync(fixture);

        JsonElement inventory = await fixture.CallAsync(
            "build_inventory",
            new
            {
                lines = new[] { new { value = 1000.0, unit = "KilowattHour", factorId } },
                scope2Method = "LocationBased",
            });

        // Location-based and market-based are separate disclosures. The response carries
        // whichever the lines actually reported and gives the other as null rather than as
        // zero, which would read as a claim that it is zero.
        //
        // Null and not absent: the output schema requires the property, so omitting it makes
        // the result fail validation against the schema this same server publishes. This
        // assertion used to demand absence, which is how that shipped.
        Assert.True(inventory.TryGetProperty("scope2LocationBased", out JsonElement locationBased));
        Assert.True(locationBased.GetProperty("value").GetDouble() > 0);

        Assert.True(inventory.TryGetProperty("scope2MarketBased", out JsonElement marketBased));
        Assert.Equal(JsonValueKind.Null, marketBased.ValueKind);
    }

    [Fact]
    public async Task Unit_conversion_is_exact_on_definitional_ratios()
    {
        JsonElement result = await fixture.CallAsync(
            "convert_units",
            new { value = 1.0, fromUnit = "CubicMetre", toUnit = "Litre" });

        Assert.Equal(1000.0, result.GetProperty("value").GetDouble());
        Assert.Equal("Volume", result.GetProperty("dimension").GetString());
    }

    /// <summary>
    /// Finds a scope 1 factor denominated in the given unit, so tests do not hard-code an id
    /// that a catalog revision could retire.
    /// </summary>
    internal static async Task<string> FindFactorAsync(McpServerFixture fixture, string unit)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        JsonElement response = await fixture.CallAsync(
            "search_emission_factors",
            new { scope = "Scope1", limit = 200 });

        foreach (JsonElement factor in response.GetProperty("factors").EnumerateArray())
        {
            if (factor.GetProperty("unit").GetString() == unit)
            {
                return factor.GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException($"No scope 1 factor denominated in {unit} is compiled into this build.");
    }

    /// <summary>
    /// Finds a scope 2 factor denominated in kilowatt hours.
    /// </summary>
    internal static async Task<string> FindScope2FactorAsync(McpServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        JsonElement response = await fixture.CallAsync(
            "search_emission_factors",
            new { scope = "Scope2", limit = 200 });

        foreach (JsonElement factor in response.GetProperty("factors").EnumerateArray())
        {
            if (factor.GetProperty("unit").GetString() == "KilowattHour")
            {
                return factor.GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException("No scope 2 factor denominated in KilowattHour is compiled into this build.");
    }

    private static void AssertClose(double expected, double actual)
    {
        double tolerance = Math.Max(Math.Abs(expected), 1) * 1e-9;

        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected} but got {actual}.");
    }
}
