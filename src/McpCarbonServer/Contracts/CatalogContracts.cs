namespace McpCarbonServer.Contracts;

/// <summary>
/// Where a factor set's numbers were published, so a figure can be traced back to a
/// citable document rather than to this server.
/// </summary>
/// <param name="Publisher">The publishing body, for example <c>DEFRA</c>.</param>
/// <param name="Title">Title of the published dataset.</param>
/// <param name="PublicationYear">Year of publication.</param>
/// <param name="Url">Link to the source document, when one is recorded.</param>
/// <param name="License">Licence the source data is published under, when recorded.</param>
public sealed record SourceInfo(
    string Publisher,
    string Title,
    int PublicationYear,
    string? Url,
    string? License);

/// <summary>
/// A published collection of emission factors compiled into this build.
/// </summary>
/// <param name="Id">Identifier used to reference the set.</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Region">Geography the set applies to, when it is region-specific.</param>
/// <param name="ValidFrom">Start of the reporting period the set covers.</param>
/// <param name="ValidTo">End of the reporting period the set covers.</param>
/// <param name="Verification">
/// Whether the numbers have been checked against the cited source. Anything other than
/// <c>Verified</c> means the set is not fit for a published disclosure.
/// </param>
/// <param name="FactorCount">How many factors the set contains.</param>
/// <param name="Source">Provenance of the set.</param>
public sealed record FactorSetSummary(
    string Id,
    string Name,
    string? Region,
    string? ValidFrom,
    string? ValidTo,
    string Verification,
    int FactorCount,
    SourceInfo Source);

/// <summary>
/// The outcome of a catalog search.
/// </summary>
/// <param name="Matched">
/// How many factors matched the filters in total, before the result was capped.
/// </param>
/// <param name="Returned">How many factors this response carries.</param>
/// <param name="Factors">The factors, capped at the requested limit.</param>
/// <remarks>
/// <paramref name="Matched"/> exists so that a capped result cannot be mistaken for a
/// complete one. A search that returned twenty-five of ninety-eight matches without saying
/// so reads exactly like a search that found twenty-five, and a caller working from the
/// second reading will state a conclusion the data does not support.
/// </remarks>
public sealed record FactorSearchResponse(
    int Matched,
    int Returned,
    System.Collections.Generic.IReadOnlyList<FactorSummary> Factors);

/// <summary>
/// One emission factor, as returned by a catalog search.
/// </summary>
/// <param name="Id">Identifier to pass to a calculation tool.</param>
/// <param name="Activity">What the factor measures, for example <c>Natural gas</c>.</param>
/// <param name="Scope">GHG Protocol scope the factor reports under.</param>
/// <param name="Scope3Category">Scope 3 category number, for scope 3 factors.</param>
/// <param name="Scope2Method">
/// Location-based or market-based, for scope 2 factors. The two are reported as separate
/// totals and are not interchangeable.
/// </param>
/// <param name="Unit">Denominator unit the factor is expressed per.</param>
/// <param name="Region">Geography the factor applies to.</param>
/// <param name="DataQuality">How direct the underlying measurement is.</param>
/// <param name="UncertaintyPercent">Reported uncertainty, when the source publishes one.</param>
/// <param name="Note">Any qualification the source attaches to the factor.</param>
/// <param name="SetId">Identifier of the set the factor belongs to.</param>
/// <param name="Verification">Verification status inherited from the set.</param>
public sealed record FactorSummary(
    string Id,
    string Activity,
    string Scope,
    int? Scope3Category,
    string? Scope2Method,
    string Unit,
    string? Region,
    string DataQuality,
    double? UncertaintyPercent,
    string? Note,
    string SetId,
    string Verification);
