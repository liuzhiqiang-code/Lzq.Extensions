namespace Lzq.Core.Utils;

public static class StringExtensions
{
    public static int? ToInt32(this string? value)
    {
        return int.TryParse(value, out int result) ? result : null;
    }

    public static int ToInt32(this string? value, int defaultValue = 0)
    {
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    public static long? ToInt64(this string? value)
    {
        return long.TryParse(value, out long result) ? result : null;
    }

    public static long ToInt64(this string? value, long defaultValue = 0)
    {
        return long.TryParse(value, out long result) ? result : defaultValue;
    }

    public static float? ToFloat(this string? value)
    {
        return float.TryParse(value, out float result) ? result : null;
    }
    public static float ToFloat(this string? value, float defaultValue = 0)
    {
        return float.TryParse(value, out float result) ? result : defaultValue;
    }

    public static double? ToDouble(this string? value)
    {
        return double.TryParse(value, out double result) ? result : null;
    }

    public static double ToDouble(this string? value, double defaultValue = 0)
    {
        return double.TryParse(value, out double result) ? result : defaultValue;
    }

    public static decimal? ToDecimal(this string? value)
    {
        return decimal.TryParse(value, out decimal result) ? result : null;
    }

    public static decimal ToDecimal(this string? value, decimal defaultValue = 0)
    {
        return decimal.TryParse(value, out decimal result) ? result : defaultValue;
    }


    public static DateTime? ToDateTime(this string value)
    {
        try
        {
            return DateTime.Parse(value);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
