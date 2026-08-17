namespace McpCarbonServer.Contracts;

/// <summary>
/// The outcome of converting a quantity between two units of the same dimension.
/// </summary>
/// <param name="Value">The converted magnitude.</param>
/// <param name="Unit">The unit the magnitude is now expressed in.</param>
/// <param name="Dimension">The physical quantity both units measure.</param>
/// <param name="OriginalValue">The magnitude that was supplied.</param>
/// <param name="OriginalUnit">The unit that was supplied.</param>
public sealed record UnitConversionResponse(
    double Value,
    string Unit,
    string Dimension,
    double OriginalValue,
    string OriginalUnit);
