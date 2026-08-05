namespace ServiceLib.Services;

public sealed class SubscriptionUpdateCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Dictionary<RequestKey, Lazy<Task<SubscriptionUpdateResult>>> _inFlightByRequest = [];
    private readonly Func<SubscriptionUpdateRequest, Task<SubscriptionUpdateResult>> _updateAsync;

    public SubscriptionUpdateCoordinator(Func<SubscriptionUpdateRequest, Task<SubscriptionUpdateResult>> updateAsync)
    {
        _updateAsync = updateAsync ?? throw new ArgumentNullException(nameof(updateAsync));
    }

    public Task<SubscriptionUpdateResult> UpdateAsync(SubscriptionUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedId = request.SubscriptionId?.Trim() ?? string.Empty;
        if (normalizedId.Length == 0)
        {
            return ExecuteSerializedAsync(request);
        }

        var key = new RequestKey(
            normalizedId,
            request.UseProxy,
            request.AllowDirectFallback,
            request.IsAutomatic);

        lock (_syncRoot)
        {
            if (_inFlightByRequest.TryGetValue(key, out var existing))
            {
                return existing.Value;
            }

            Lazy<Task<SubscriptionUpdateResult>> pending = null!;
            pending = new Lazy<Task<SubscriptionUpdateResult>>(
                () => ExecuteAndRemoveAsync(key, pending, request),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _inFlightByRequest.Add(key, pending);
            return pending.Value;
        }
    }

    private async Task<SubscriptionUpdateResult> ExecuteAndRemoveAsync(
        RequestKey key,
        Lazy<Task<SubscriptionUpdateResult>> owner,
        SubscriptionUpdateRequest request)
    {
        try
        {
            return await ExecuteSerializedAsync(request);
        }
        finally
        {
            lock (_syncRoot)
            {
                if (_inFlightByRequest.TryGetValue(key, out var current)
                    && ReferenceEquals(current, owner))
                {
                    _inFlightByRequest.Remove(key);
                }
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

    private readonly record struct RequestKey(
        string SubscriptionId,
        bool UseProxy,
        bool AllowDirectFallback,
        bool IsAutomatic);
}
