using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using GhgAccounting;
using GhgAccounting.Factors;
using McpCarbonServer.Contracts;
using McpCarbonServer.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpCarbonServer.Resources;

/// <summary>
/// The catalog as attachable context.
/// </summary>
/// <remarks>
/// Tools are what the model calls to answer a question; resources are what a person
/// attaches when they want the material itself in the conversation - writing a methodology
/// note, checking a licence before redistributing a figure, auditing which potentials a
/// disclosure was aggregated under. Everything here is projected from the compiled catalog
/// rather than written out by hand, so a resource cannot drift from what the tools compute.
/// </remarks>
[McpServerResourceType]
public static class CatalogResources
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Every factor set compiled into this build, with its provenance and licence.
    /// </summary>
    /// <returns>The catalog index as JSON.</returns>
    [McpServerResource(
        UriTemplate = "carbon://factor-sets",
        Name = "factor_sets",
        Title = "Emission factor datasets",
        MimeType = "application/json")]
    public static string FactorSets() =>
        JsonSerializer.Serialize(
            FactorCatalog.Sets.Select(Mapping.ToFactorSetSummary).ToArray(),
            SerializerOptions);

    /// <summary>
    /// One factor set in full, including every factor it publishes.
    /// </summary>
    /// <param name="setId">Identifier of the set, as listed in the catalog index.</param>
    /// <returns>The set and its factors as JSON.</returns>
    [McpServerResource(
        UriTemplate = "carbon://factor-sets/{setId}",
        Name = "factor_set",
        Title = "One emission factor dataset in full",
        MimeType = "application/json")]
    public static string FactorSet(string setId)
    {
        FactorSet? set = FactorCatalog.Sets
            .FirstOrDefault(candidate => candidate.Id.Equals(setId, StringComparison.OrdinalIgnoreCase));

        if (set is null)
        {
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "No factor set with id '{0}' is compiled into this build. Read carbon://factor-sets for the available ids.",
                    setId));
        }

        return JsonSerializer.Serialize(
            new
            {
                Set = Mapping.ToFactorSetSummary(set),
                Factors = set.Factors.Select(Mapping.ToFactorSummary).ToArray(),
            },
            SerializerOptions);
    }

    /// <summary>
    /// The global warming potentials compiled into this build for one assessment report.
    /// </summary>
    /// <param name="gwpSet">The assessment report, for example <c>Ar6</c>.</param>
    /// <returns>The potentials as JSON.</returns>
    /// <remarks>
    /// Worth attaching when a disclosure has to state which potentials it used: the answer
    /// is a property of the numbers actually shipped, not of what the report says in
    /// general.
    /// </remarks>
    [McpServerResource(
        UriTemplate = "carbon://gwp/{gwpSet}",
        Name = "gwp_table",
        Title = "Global warming potentials for one assessment report",
        MimeType = "application/json")]
    public static string GwpTableResource(string gwpSet)
    {
        if (!Enum.TryParse(gwpSet, ignoreCase: true, out GwpSet parsed))
        {
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is not an assessment report this build knows. Compiled sets: {1}.",
                    gwpSet,
                    string.Join(", ", GwpTable.All.Select(table => table.Set))));
        }

        GwpTable? table = GwpTable.All.FirstOrDefault(candidate => candidate.Set == parsed);

        if (table is null)
        {
            throw new McpException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "No potentials for {0} are compiled into this build. Compiled sets: {1}.",
                    parsed,
                    string.Join(", ", GwpTable.All.Select(candidate => candidate.Set))));
        }

        IReadOnlyList<object> values = table.Values
            .Select(value => (object)new
            {
                Gas = value.Gas.ToString(),
                Gwp = value.Gwp,
                value.Formula,
                value.SourceTable,
            })
            .ToArray();

        return JsonSerializer.Serialize(
            new
            {
                Set = table.Set.ToString(),
                table.Name,
                table.TimeHorizonYears,
                table.IncludesClimateCarbonFeedback,
                Verification = table.Verification.ToString(),
                Source = Mapping.ToSourceInfo(table.Source),
                Values = values,
            },
            SerializerOptions);
    }
}
