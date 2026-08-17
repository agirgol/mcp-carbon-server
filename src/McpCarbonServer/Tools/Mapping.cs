using System.Collections.Generic;
using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Catalog;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using McpCarbonServer.Contracts;

namespace McpCarbonServer.Tools;

/// <summary>
/// Projects library types onto the wire contracts. Kept in one place so the tools stay
/// readable and so the shape a client sees is not decided incidentally in five methods.
/// </summary>
internal static class Mapping
{
    internal static QuantityValue ToQuantityValue(Quantity quantity) =>
        new(quantity.Value, quantity.Unit.ToString());

    internal static SourceInfo ToSourceInfo(CatalogSource source) =>
        new(source.Publisher, source.Title, source.PublicationYear, source.Url, source.License);

    internal static FactorSetSummary ToFactorSetSummary(FactorSet set) =>
        new(
            set.Id,
            set.Name,
            set.Region,
            set.ValidFrom,
            set.ValidTo,
            set.Verification.ToString(),
            set.Factors.Count,
            ToSourceInfo(set.Source));

    internal static FactorSummary ToFactorSummary(EmissionFactor factor) =>
        new(
            factor.Id,
            factor.Activity,
            factor.Scope.ToString(),
            factor.Scope3Category,
            factor.Scope2Method?.ToString(),
            factor.Unit.ToString(),
            factor.Region,
            factor.DataQuality.ToString(),
            factor.UncertaintyPercent,
            factor.Note,
            factor.Set.Id,
            factor.Set.Verification.ToString());

    internal static CalculationResponse ToCalculationResponse(EmissionResult result)
    {
        IReadOnlyList<GasBreakdown> gases = result.Gases
            .Select(gas => new GasBreakdown(
                gas.Gas.ToString(),
                ToQuantityValue(gas.Mass),
                ToQuantityValue(gas.Co2e)))
            .ToArray();

        return new CalculationResponse(
            result.Factor.Id,
            ToQuantityValue(result.Activity),
            ToQuantityValue(result.Co2e),
            result.Scope.ToString(),
            result.Scope2Method?.ToString(),
            result.Scope3Category,
            gases,
            ToQuantityValue(result.BiogenicCarbon),
            result.GwpSet.ToString(),
            result.DataQuality.ToString(),
            result.UncertaintyPercent,
            ToSourceInfo(result.Factor.Set.Source),
            result.Factor.Set.Verification.ToString());
    }
}
