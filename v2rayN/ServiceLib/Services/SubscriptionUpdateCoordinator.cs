namespace ServiceLib.Services;

public sealed class SubscriptionUpdateCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Lazy<Task<SubscriptionUpdateResult>>> _inFlightBySubscriptionId = new(StringComparer.Ordinal);
    private readonly Func<SubscriptionUpdateRequest, Task<SubscriptionUpdateResult>> _updateAsync;

    public SubscriptionUpdateCoordinator(Func<SubscriptionUpdateRequest, Task<SubscriptionUpdateResult>> updateAsync)
    {
        _updateAsync = updateAsync ?? throw new ArgumentNullException(nameof(updateAsync));
    }

    public Task<SubscriptionUpdateResult> UpdateAsync(SubscriptionUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var initialId = request.SubscriptionId?.Trim() ?? string.Empty;
        if (initialId.Length == 0)
        {
            return ExecuteSerializedAsync(request);
        }

        lock (_syncRoot)
        {
            if (_inFlightBySubscriptionId.TryGetValue(initialId, out var existing))
            {
                return existing.Value;
            }

            var pending = new Lazy<Task<SubscriptionUpdateResult>>(
                () => ExecuteAndRemoveAsync(initialId, request),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _inFlightBySubscriptionId.Add(initialId, pending);
            return pending.Value;
        }
    }

    private async Task<SubscriptionUpdateResult> ExecuteAndRemoveAsync(string initialId, SubscriptionUpdateRequest request)
    {
        try
        {
            return await ExecuteSerializedAsync(request);
        }
        finally
        {
            lock (_syncRoot)
            {
                _inFlightBySubscriptionId.Remove(initialId);
            }
        }
    }

    private async Task<SubscriptionUpdateResult> ExecuteSerializedAsync(SubscriptionUpdateRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            return await _updateAsync(request);
        }
        catch
        {
            return SubscriptionUpdateResult.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }
}
