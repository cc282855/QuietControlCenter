namespace ServiceLib.Common;

public static class SubscriptionSourceDisplay
{
    public static string Format(string? remarks)
    {
        var name = remarks?.Trim() ?? string.Empty;
        if (name.IsNotEmpty())
        {
            return $"订阅：{name}";
        }
        return "订阅：未命名订阅";
    }
}
