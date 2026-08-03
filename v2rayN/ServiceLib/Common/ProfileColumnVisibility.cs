namespace ServiceLib.Common;

public static class ProfileColumnVisibility
{
    public const string ConfigType = "ConfigType";
    public const string Remarks = "Remarks";
    public const string Address = "Address";
    public const string Port = "Port";
    public const string Network = "Network";
    public const string StreamSecurity = "StreamSecurity";
    public const string Delay = "Delay";
    public const string SpeedVal = "SpeedVal";

    private static readonly string[] ColumnNames =
    [
        ConfigType, Remarks, Address, Port, Network, StreamSecurity, Delay, SpeedVal
    ];

    public static IReadOnlyList<string> Columns => ColumnNames;

    private static readonly HashSet<string> Supported = new(ColumnNames, StringComparer.Ordinal);

    public static string Canonicalize(string? name) => name switch
    {
        "DelayVal" => Delay,
        _ => name ?? string.Empty
    };

    public static bool IsSupported(string? name) => Supported.Contains(Canonicalize(name));

    public static IReadOnlyList<string> NormalizeHiddenColumns(IEnumerable<string>? hiddenColumns)
    {
        if (hiddenColumns is null)
        {
            return [];
        }

        return hiddenColumns
            .Select(Canonicalize)
            .Where(Supported.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsVisible(IEnumerable<string>? hiddenColumns, string columnName) =>
        !NormalizeHiddenColumns(hiddenColumns).Contains(Canonicalize(columnName), StringComparer.Ordinal);

    public static List<string> GetHiddenColumns(IEnumerable<KeyValuePair<string, bool>> visibility) =>
        visibility
            .Where(item => IsSupported(item.Key) && !item.Value)
            .Select(item => Canonicalize(item.Key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => Array.IndexOf(ColumnNames, name))
            .ToList();
}
