namespace McpCarbonServer.Contracts;

/// <summary>
/// A magnitude together with the unit it is expressed in. Emissions figures are never
/// returned as a bare number, because a number whose unit lives only in a field name is
/// the most common way a wrong figure reaches a report.
/// </summary>
/// <param name="Value">The magnitude.</param>
/// <param name="Unit">The unit the magnitude is expressed in.</param>
public sealed record QuantityValue(double Value, string Unit);

/// <summary>
/// One greenhouse gas's contribution to a result, before and after applying its global
/// warming potential.
/// </summary>
/// <param name="Gas">The gas.</param>
/// <param name="Mass">Mass of the gas itself.</param>
/// <param name="Co2e">That mass expressed as carbon dioxide equivalent.</param>
public sealed record GasBreakdown(string Gas, QuantityValue Mass, QuantityValue Co2e);

/// <summary>
/// The result of applying one emission factor to one activity figure.
/// </summary>
/// <param name="FactorId">The factor that was applied.</param>
/// <param name="Activity">The activity figure, as it was interpreted after unit conversion.</param>
/// <param name="Co2e">Total carbon dioxide equivalent.</param>
/// <param name="Scope">GHG Protocol scope the figure reports under.</param>
/// <param name="Scope2Method">Location-based or market-based, for scope 2 figures.</param>
/// <param name="Scope3Category">Scope 3 category number, for scope 3 figures.</param>
/// <param name="Gases">Per-gas breakdown behind the total.</param>
/// <param name="BiogenicCarbon">
/// Biogenic carbon dioxide, reported separately from the total because the GHG Protocol
/// requires it to be disclosed outside the scopes rather than added to them.
/// </param>
/// <param name="GwpSet">Assessment report whose potentials were used to aggregate gases.</param>
/// <param name="DataQuality">How direct the underlying factor is.</param>
/// <param name="UncertaintyPercent">Reported uncertainty, when the source publishes one.</param>
/// <param name="Source">Provenance of the factor that produced the figure.</param>
/// <param name="Verification">
/// Verification status of the factor set. A figure derived from an unverified set must
/// not be presented as a disclosure.
/// </param>
public sealed record CalculationResponse(
    string FactorId,
    QuantityValue Activity,
    QuantityValue Co2e,
    string Scope,
    string? Scope2Method,
    int? Scope3Category,
    System.Collections.Generic.IReadOnlyList<GasBreakdown> Gases,
    QuantityValue BiogenicCarbon,
    string GwpSet,
    string DataQuality,
    double? UncertaintyPercent,
    SourceInfo Source,
    string Verification);
