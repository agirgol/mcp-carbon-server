using System.Collections.Generic;
using GhgAccounting.Units;

namespace McpCarbonServer.Contracts;

/// <summary>
/// One line of activity data to be added to an inventory.
/// </summary>
/// <param name="Value">The activity magnitude.</param>
/// <param name="Unit">
/// The unit the magnitude is expressed in. Any unit measuring the same physical quantity
/// as the factor's denominator is accepted and converted.
/// </param>
/// <param name="FactorId">Identifier of the factor to apply, from the catalog.</param>
public sealed record InventoryLine(double Value, Unit Unit, string FactorId);

/// <summary>
/// A scope 3 subtotal for one of the fifteen GHG Protocol categories.
/// </summary>
/// <param name="Category">The category number, 1 to 15.</param>
/// <param name="Co2e">Total for that category.</param>
public sealed record Scope3CategoryBreakdown(int Category, QuantityValue Co2e);

/// <summary>
/// Aggregated emissions across every line added to an inventory.
/// </summary>
/// <param name="GwpSet">Assessment report the whole inventory was aggregated under.</param>
/// <param name="LineCount">How many activity lines were aggregated.</param>
/// <param name="Scope1">Direct emissions.</param>
/// <param name="Scope2LocationBased">
/// Purchased energy under the location-based method, or <see langword="null"/> when no
/// line reported it.
/// </param>
/// <param name="Scope2MarketBased">
/// Purchased energy under the market-based method, or <see langword="null"/> when no line
/// reported it.
/// </param>
/// <param name="Scope3">Value chain emissions.</param>
/// <param name="Scope3Uncategorised">
/// Scope 3 emissions from factors that carry no category number, kept separate so they
/// are visibly outside the categorised breakdown rather than silently missing from it.
/// </param>
/// <param name="Scope3ByCategory">Scope 3 split by GHG Protocol category.</param>
/// <param name="Total">
/// Inventory total under the requested scope 2 method. Scope 2 is reported both ways and
/// only one of them belongs in any given total.
/// </param>
/// <param name="Scope2MethodUsedForTotal">Which scope 2 method the total was taken under.</param>
/// <param name="BiogenicCarbon">Biogenic carbon dioxide, disclosed outside the scopes.</param>
/// <param name="UncertaintyPercent">Propagated uncertainty, when every contributing factor reports one.</param>
public sealed record InventoryResponse(
    string GwpSet,
    int LineCount,
    QuantityValue Scope1,
    QuantityValue? Scope2LocationBased,
    QuantityValue? Scope2MarketBased,
    QuantityValue Scope3,
    QuantityValue Scope3Uncategorised,
    IReadOnlyList<Scope3CategoryBreakdown> Scope3ByCategory,
    QuantityValue Total,
    string Scope2MethodUsedForTotal,
    QuantityValue BiogenicCarbon,
    double? UncertaintyPercent);
