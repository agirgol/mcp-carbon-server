using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GhgAccounting;
using GhgAccounting.Factors;
using McpCarbonServer.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpCarbonServer.Tools;

/// <summary>
/// Tools for inspecting the emission factor catalog. A calculation needs a factor id, and
/// these are how a client finds one without guessing.
/// </summary>
[McpServerToolType]
public static class CatalogTools
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 200;

    /// <summary>
    /// Lists every factor set compiled into this build.
    /// </summary>
    /// <returns>One summary per set, including provenance and verification status.</returns>
    [McpServerTool(Name = "list_factor_sets")]
    [Description(
        "List every emission factor dataset compiled into this build, with its publisher, " +
        "the geography and reporting period it covers, and whether its numbers have been " +
        "verified against the cited source. Call this to find out which datasets and " +
        "regions are available before searching for factors.")]
    public static IReadOnlyList<FactorSetSummary> ListFactorSets() =>
        FactorCatalog.Sets.Select(Mapping.ToFactorSetSummary).ToArray();

    /// <summary>
    /// Searches the catalog for factors matching the supplied filters.
    /// </summary>
    /// <param name="query">Substring matched against factor id and activity name.</param>
    /// <param name="scope">Restricts results to one GHG Protocol scope.</param>
    /// <param name="region">Substring matched against the factor's region.</param>
    /// <param name="setId">Restricts results to one factor set.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <returns>Matching factors, capped at <paramref name="limit"/>.</returns>
    [McpServerTool(Name = "search_emission_factors")]
    [Description(
        "Search the emission factor catalog and return the factor ids a calculation needs. " +
        "All filters are optional and combine with AND. Prefer searching by activity " +
        "wording (for example 'natural gas', 'diesel', 'electricity') over guessing an id.")]
    public static IReadOnlyList<FactorSummary> SearchEmissionFactors(
        [Description("Case-insensitive substring matched against the factor id and its activity name. Omit to list everything matching the other filters.")]
        string? query = null,
        [Description("Restrict to one GHG Protocol scope: Scope1 for direct emissions, Scope2 for purchased energy, Scope3 for value chain.")]
        Scope? scope = null,
        [Description("Case-insensitive substring matched against the factor's geography, for example 'UK' or 'DE'.")]
        string? region = null,
        [Description("Restrict to one factor set id, as returned by list_factor_sets.")]
        string? setId = null,
        [Description("Maximum number of factors to return. Defaults to 25, capped at 200.")]
        int limit = DefaultLimit)
    {
        if (limit <= 0)
        {
            throw new McpException("limit must be greater than zero.");
        }

        IEnumerable<EmissionFactor> matches = FactorCatalog.Factors;

        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches.Where(factor =>
                factor.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                factor.Activity.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (scope is not null)
        {
            matches = matches.Where(factor => factor.Scope == scope.Value);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            matches = matches.Where(factor =>
                factor.Region is not null &&
                factor.Region.Contains(region, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(setId))
        {
            matches = matches.Where(factor =>
                factor.Set.Id.Equals(setId, StringComparison.OrdinalIgnoreCase));
        }

        return matches
            .Take(Math.Min(limit, MaxLimit))
            .Select(Mapping.ToFactorSummary)
            .ToArray();
    }
}
