using System.Collections.Concurrent;

namespace VacationPlanner.Application.Services;

// Shared across request scopes; a failed removal must not make the old value usable again.
public sealed class BalanceCacheState
{
    private readonly ConcurrentDictionary<string, byte> dirty = new();
    public bool IsDirty(string key) => dirty.ContainsKey(key);
    public void MarkDirty(string key) => dirty[key] = 0;
    public void Clear(string key) => dirty.TryRemove(key, out _);
}
