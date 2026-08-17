using System.ComponentModel;
using GhgAccounting.Units;
using McpCarbonServer.Contracts;
using ModelContextProtocol.Server;

namespace McpCarbonServer.Tools;

/// <summary>
/// Unit conversion, exposed on its own so a client can normalise activity data before
/// reporting it without having to run a calculation to do so.
/// </summary>
[McpServerToolType]
public static class UnitTools
{
    /// <summary>
    /// Converts a magnitude between two units measuring the same physical quantity.
    /// </summary>
    /// <param name="value">The magnitude to convert.</param>
    /// <param name="fromUnit">The unit the magnitude is expressed in.</param>
    /// <param name="toUnit">The unit to express the result in.</param>
    /// <returns>The converted quantity and the dimension both units share.</returns>
    [McpServerTool(Name = "convert_units")]
    [Description(
        "Convert a quantity between two units of the same physical dimension - energy, " +
        "volume, mass, distance, freight or passenger transport. Converting across " +
        "dimensions fails rather than guessing a density or calorific value. Note that " +
        "calculation tools convert activity data themselves, so this is only needed when " +
        "normalising figures for reporting.")]
    public static UnitConversionResponse ConvertUnits(
        [Description("The magnitude to convert.")] double value,
        [Description("The unit the magnitude is currently expressed in.")] Unit fromUnit,
        [Description("The unit to express the result in.")] Unit toUnit)
    {
        double converted = UnitConverter.Convert(value, fromUnit, toUnit);

        return new UnitConversionResponse(
            converted,
            toUnit.ToString(),
            UnitConverter.GetDimension(toUnit).ToString(),
            value,
            fromUnit.ToString());
    }
}
