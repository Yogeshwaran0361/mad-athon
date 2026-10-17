namespace ReworkGuard.Domain.Common;

/// <summary>Rounding and precision rules shared by every money and hours calculation.</summary>
public static class Precision
{
    public const int Scale = 2;

    public static decimal Round2(decimal value) => decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    public static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, Scale) == value;
}
