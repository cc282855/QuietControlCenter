namespace ServiceLib.Services.Statistics;

public sealed class ClashTrafficSnapshotCalculator
{
    private readonly Dictionary<string, (ulong Up, ulong Down)> _previous = new(StringComparer.Ordinal);
    private bool _hasBaseline;

    public (long ProxyUp, long ProxyDown, long DirectUp, long DirectDown, bool HasBaseline) GetDelta(ClashConnections snapshot)
    {
        long proxyUp = 0;
        long proxyDown = 0;
        long directUp = 0;
        long directDown = 0;
        var hadBaseline = _hasBaseline;
        var currentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in snapshot.connections ?? [])
        {
            if (connection.id.IsNullOrEmpty())
            {
                continue;
            }

            currentIds.Add(connection.id);
            if (_previous.TryGetValue(connection.id, out var previous))
            {
                var up = (long)(connection.upload >= previous.Up ? connection.upload - previous.Up : 0);
                var down = (long)(connection.download >= previous.Down ? connection.download - previous.Down : 0);
                if (IsDirect(connection))
                {
                    directUp += up;
                    directDown += down;
                }
                else
                {
                    proxyUp += up;
                    proxyDown += down;
                }
            }
            else if (hadBaseline)
            {
                // The connection was created after the preceding snapshot, so
                // its current counters belong to this sampling window.
                var up = (long)Math.Min(connection.upload, (ulong)long.MaxValue);
                var down = (long)Math.Min(connection.download, (ulong)long.MaxValue);
                if (IsDirect(connection))
                {
                    directUp += up;
                    directDown += down;
                }
                else
                {
                    proxyUp += up;
                    proxyDown += down;
                }
            }
            _previous[connection.id] = (connection.upload, connection.download);
        }

        foreach (var staleId in _previous.Keys.Where(id => !currentIds.Contains(id)).ToList())
        {
            _previous.Remove(staleId);
        }

        _hasBaseline = true;
        return (proxyUp, proxyDown, directUp, directDown, hadBaseline);
    }

    public void Reset()
    {
        _previous.Clear();
        _hasBaseline = false;
    }

    private static bool IsDirect(ConnectionItem connection) =>
        connection.chains?.Any(chain => chain.Equals(Global.DirectTag, StringComparison.OrdinalIgnoreCase)) == true
        || string.Equals(connection.rule, "DIRECT", StringComparison.OrdinalIgnoreCase);
}
