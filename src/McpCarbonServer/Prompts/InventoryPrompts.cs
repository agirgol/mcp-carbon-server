using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpCarbonServer.Prompts;

/// <summary>
/// Starting points for the two jobs this server exists to support: assembling an inventory,
/// and checking whether a figure is fit to disclose.
/// </summary>
/// <remarks>
/// Unlike the resources, these are authored rather than derived - a prompt is guidance by
/// definition. What they are careful about is scope: they describe the procedure and the
/// standard's requirements, and leave every number to the tools.
/// </remarks>
[McpServerPromptType]
public static class InventoryPrompts
{
    /// <summary>
    /// Sets up an inventory-building session for one organisation and reporting year.
    /// </summary>
    /// <param name="organisation">The reporting organisation.</param>
    /// <param name="reportingYear">The reporting year.</param>
    /// <param name="region">Where the organisation operates, when it is known.</param>
    /// <returns>A single user message framing the work.</returns>
    [McpServerPrompt(
        Name = "ghg_inventory_intake",
        Title = "Assemble a GHG inventory")]
    [Description(
        "Frame a session that collects activity data and turns it into a GHG Protocol " +
        "inventory, with factors looked up from the catalog rather than recalled.")]
    public static IReadOnlyList<PromptMessage> InventoryIntake(
        [Description("The reporting organisation.")] string organisation,
        [Description("The reporting year, for example 2026.")] int reportingYear,
        [Description("Where the organisation operates, for example 'UK' or 'Türkiye'. Optional.")]
        string? region = null)
    {
        string where = string.IsNullOrWhiteSpace(region)
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, ", operating in {0}", region);

        string text = string.Format(
            CultureInfo.InvariantCulture,
            """
            Help {0} assemble a greenhouse gas inventory for {1}{2}.

            Work from the catalog, not from memory. Concretely:

            1. Establish what is in the boundary before calculating anything. Ask for the
               activity data you are missing; do not assume quantities, and do not invent a
               figure to fill a gap.
            2. For each activity, find a factor with search_emission_factors. Prefer a
               dataset whose region and reporting period match the organisation and the
               year. Say which one you picked and why when more than one would fit.
            3. Build the inventory in a single build_inventory call, so every line is
               aggregated under one assessment report. An inventory that mixes assessment
               reports is not a valid disclosure.
            4. Report scope 2 under both the location-based and market-based methods when
               the data supports both. They are separate disclosures and only one belongs
               in a given total.
            5. Keep biogenic carbon dioxide outside the scope totals and disclose it
               separately, as the standard requires.
            6. For every figure, state the dataset it came from, its publication year and
               its verification status. Treat anything from an unverified set as not fit
               for disclosure and say so plainly.

            If some part of the boundary cannot be quantified with the factors available,
            report that as a gap rather than approximating around it.
            """,
            organisation,
            reportingYear,
            where);

        return [UserMessage(text)];
    }

    /// <summary>
    /// Reviews a draft figure or inventory for the things a disclosure has to carry.
    /// </summary>
    /// <param name="figures">The draft figures, as the user has them.</param>
    /// <returns>A single user message framing the review.</returns>
    [McpServerPrompt(
        Name = "disclosure_review",
        Title = "Check a figure is fit to disclose")]
    [Description(
        "Review draft emissions figures against what a GHG Protocol disclosure requires, " +
        "reporting what is missing rather than filling the gaps.")]
    public static IReadOnlyList<PromptMessage> DisclosureReview(
        [Description("The draft figures or inventory to review, as they currently stand.")]
        string figures)
    {
        string text = string.Format(
            CultureInfo.InvariantCulture,
            """
            Review the figures below against what a GHG Protocol disclosure has to carry.
            Report what is missing; do not fill the gaps yourself or restate the numbers as
            though the gaps were closed.

            Check each of these:

            - Is the emission factor behind every figure identified, with its dataset,
              publication year and verification status? A number with no traceable factor
              is not a disclosure.
            - Is one assessment report used throughout, and is it stated? Mixing AR5 and
              AR6 within an inventory invalidates it.
            - Is scope 2 reported under both methods where the data allows, and is it clear
              which one any headline total uses?
            - Is biogenic carbon dioxide reported separately rather than inside the scope
              totals?
            - Are the units stated on every figure?
            - Does the geography and reporting period of each factor match the activity it
              was applied to?

            Use the catalog tools to verify anything you can check rather than judging it
            by eye. Where a factor id is given, look it up.

            Figures under review:

            {0}
            """,
            figures);

        return [UserMessage(text)];
    }

    private static PromptMessage UserMessage(string text) =>
        new() { Role = Role.User, Content = new TextContentBlock { Text = text } };
}
