using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using GhgAccounting;
using GhgAccounting.Calculation;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using McpCarbonServer.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpCarbonServer.Tools;

/// <summary>
/// Calculation tools: one activity line at a time, or a whole inventory aggregated by
/// scope.
/// </summary>
[McpServerToolType]
public static class EmissionTools
{
    /// <summary>
    /// Applies one catalog factor to one activity figure.
    /// </summary>
    /// <param name="value">The activity magnitude.</param>
    /// <param name="unit">The unit the magnitude is expressed in.</param>
    /// <param name="factorId">Identifier of the factor to apply.</param>
    /// <param name="gwpSet">Assessment report whose global warming potentials to use.</param>
    /// <returns>The emissions, with the per-gas breakdown and the factor's provenance.</returns>
    [McpServerTool(
        Name = "calculate_emissions",
        Title = "Calculate emissions from activity data",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Apply one emission factor to one activity figure and return the CO2e result with " +
        "its per-gas breakdown and the source the factor came from. The activity may be " +
        "given in any unit measuring the same physical quantity as the factor - litres " +
        "against a per-cubic-metre factor is converted, litres against a per-kWh factor is " +
        "rejected. Get factor ids from search_emission_factors.")]
    public static CalculationResponse CalculateEmissions(
        [Description("The activity magnitude, for example 1500 for 1500 kWh of electricity.")]
        double value,
        [Description("The unit the activity magnitude is expressed in.")]
        Unit unit,
        [Description("Identifier of the emission factor to apply, as returned by search_emission_factors.")]
        string factorId,
        [Description("Which IPCC assessment report's global warming potentials to aggregate gases with. AR6 is current; use AR5 only when matching an existing disclosure.")]
        GwpSet gwpSet = GwpSet.Ar6)
    {
        EmissionFactor factor = ResolveFactor(factorId);
        EmissionCalculator calculator = new(gwpSet);

        return Mapping.ToCalculationResponse(Calculate(calculator, value, unit, factor));
    }

    /// <summary>
    /// Aggregates several activity lines into a scope 1/2/3 inventory.
    /// </summary>
    /// <param name="lines">The activity lines to aggregate.</param>
    /// <param name="gwpSet">Assessment report whose global warming potentials to use.</param>
    /// <param name="scope2Method">Which scope 2 method the headline total is taken under.</param>
    /// <returns>Totals by scope, the scope 3 category breakdown, and the inventory total.</returns>
    [McpServerTool(
        Name = "build_inventory",
        Title = "Build a scope 1/2/3 inventory",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Aggregate several activity lines into a GHG Protocol inventory: scope 1, scope 2 " +
        "reported both location-based and market-based, and scope 3 broken down by " +
        "category. Every line is aggregated under one assessment report, because an " +
        "inventory that mixes them is not a valid disclosure. Biogenic carbon is reported " +
        "separately from the total rather than added to it.")]
    public static InventoryResponse BuildInventory(
        [Description("The activity lines to aggregate. Each carries a magnitude, its unit, and the id of the factor to apply.")]
        IReadOnlyList<InventoryLine> lines,
        [Description("Which IPCC assessment report's global warming potentials to aggregate gases with. AR6 is current; use AR5 only when matching an existing disclosure.")]
        GwpSet gwpSet = GwpSet.Ar6,
        [Description("Which scope 2 method the headline total is taken under. Both are always reported separately; only one belongs in a total.")]
        Scope2Method scope2Method = Scope2Method.LocationBased)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new McpException("At least one activity line is required to build an inventory.");
        }

        EmissionCalculator calculator = new(gwpSet);
        InventoryBuilder builder = calculator.CreateInventory();

        foreach (InventoryLine line in lines)
        {
            EmissionFactor factor = ResolveFactor(line.FactorId);
            builder.Add(Calculate(calculator, line.Value, line.Unit, factor));
        }

        Inventory inventory = builder.Build();

        Quantity total;
        try
        {
            total = inventory.TotalWith(scope2Method);
        }
        catch (Scope2MethodNotReportedException)
        {
            // Asking for a market-based total when no line carries a market-based factor is
            // a reporting error, not a calculation one: the honest answer is that the figure
            // does not exist, not a total that quietly omits scope 2.
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "No line in this inventory reports scope 2 under the {0} method, so no total can be produced under it. " +
                    "Either supply a {0} factor for purchased energy, or request the other method.",
                    scope2Method));
        }

        IReadOnlyList<Scope3CategoryBreakdown> byCategory = inventory.Scope3ByCategory
            .Select(category => new Scope3CategoryBreakdown(
                category.Category,
                Mapping.ToQuantityValue(category.Co2e)))
            .ToArray();

        return new InventoryResponse(
            inventory.GwpSet.ToString(),
            inventory.Entries.Count,
            Mapping.ToQuantityValue(inventory.Scope1),
            inventory.Scope2.HasLocationBased ? Mapping.ToQuantityValue(inventory.Scope2.LocationBased) : null,
            inventory.Scope2.HasMarketBased ? Mapping.ToQuantityValue(inventory.Scope2.MarketBased) : null,
            Mapping.ToQuantityValue(inventory.Scope3),
            Mapping.ToQuantityValue(inventory.Scope3Uncategorised),
            byCategory,
            Mapping.ToQuantityValue(total),
            scope2Method.ToString(),
            Mapping.ToQuantityValue(inventory.BiogenicCarbon),
            inventory.UncertaintyPercentFor(scope2Method));
    }

    /// <summary>
    /// Resolves a factor id, replacing the catalog's <see cref="KeyNotFoundException"/> with
    /// a message that tells the caller how to find a real id.
    /// </summary>
    /// <remarks>
    /// These are thrown as <see cref="McpException"/> rather than as ordinary argument
    /// exceptions on purpose. The server reports an unhandled exception to the client as a
    /// bare "an error occurred", withholding the message so that internal detail cannot
    /// leak; the message on an <see cref="McpException"/> is the part contracted to travel.
    /// A recoverable mistake the caller can act on is worth nothing if the guidance is
    /// swallowed on the way out.
    /// </remarks>
    private static EmissionFactor ResolveFactor(string factorId)
    {
        if (string.IsNullOrWhiteSpace(factorId))
        {
            throw new McpException("A factor id is required. Use search_emission_factors to find one.");
        }

        if (!FactorCatalog.TryGet(factorId, out EmissionFactor? factor) || factor is null)
        {
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "No emission factor with id '{0}' is compiled into this build. " +
                    "Use search_emission_factors to find a valid id, or list_factor_sets to see which datasets are available.",
                    factorId));
        }

        return factor;
    }

    /// <summary>
    /// Applies a factor, translating the library's refusals into messages that name what
    /// went wrong and what to do instead.
    /// </summary>
    /// <remarks>
    /// Every exception the calculator raises here is a refusal rather than a fault: the
    /// library declines to invent a conversion, a gas split, or a warming potential the
    /// publisher never gave. Each one is recoverable by the caller — a different unit, a
    /// different assessment report, a different factor — which is exactly why the guidance
    /// has to survive the trip out. Left untranslated it reaches the client as "an error
    /// occurred", and a caller that cannot tell a refusal from a crash retries the same
    /// call or gives up on a factor that would have worked.
    /// </remarks>
    private static EmissionResult Calculate(
        EmissionCalculator calculator,
        double value,
        Unit unit,
        EmissionFactor factor)
    {
        try
        {
            return calculator.Calculate(new Quantity(value, unit), factor);
        }
        catch (GwpBasisMismatchException ex)
        {
            // Datasets commonly publish a single CO2e figure with no per-gas breakdown —
            // most of DEFRA's material-use and waste tables are like this. Such a figure
            // is only meaningful under the assessment report it was aggregated with, and
            // re-aggregating it would mean inventing the split the publisher withheld.
            // Naming the set that does work turns a dead end into one more call.
            throw new McpException(
                ex.PublishedBasis is GwpSet basis
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Factor '{0}' publishes no gas breakdown, only an aggregate CO2e figure computed under {1}, " +
                        "so it cannot be re-aggregated under {2}. Request this factor with gwpSet {1}, or choose a " +
                        "factor that publishes its gases if the inventory must be {2}. Note that an inventory must " +
                        "state a single assessment report, so mixing the two is not an option.",
                        factor.Id,
                        basis,
                        calculator.GwpSet)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "Factor '{0}' publishes an aggregate CO2e figure without stating which assessment report " +
                        "aggregated it, and without a gas breakdown to re-aggregate. It cannot be used in a {1} " +
                        "calculation, or any other; choose a factor that publishes its gases.",
                        factor.Id,
                        calculator.GwpSet));
        }
        catch (GasNotCoveredException ex)
        {
            // The set covers the gas or it does not; treating a missing potential as zero
            // would silently understate the figure, which is the one outcome worth
            // refusing outright.
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Factor '{0}' includes {1}, for which the {2} GWP set publishes no potential. " +
                    "Use an assessment report that covers this gas, or choose a factor without it — " +
                    "the gas cannot be treated as zero.",
                    factor.Id,
                    ex.Gas,
                    ex.Set));
        }
        catch (UnitConversionException)
        {
            // The library refuses to convert across dimensions rather than assume a density
            // or a calorific value. Say which two quantities failed to line up, so the
            // caller can pick a different unit or a different factor instead of retrying
            // the same call.
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Factor '{0}' is published per {1}, which measures {2}, but the activity was given in {3}, which measures {4}. " +
                    "Supply the activity in a unit of {2}, or choose a factor published per {4}.",
                    factor.Id,
                    factor.Unit,
                    UnitConverter.GetDimension(factor.Unit),
                    unit,
                    UnitConverter.GetDimension(unit)));
        }
    }
}
