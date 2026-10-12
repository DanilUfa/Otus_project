using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Configuration;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>
/// Минимальный IConfiguration поверх Dictionary — без пакета configuration:
/// indexer + GetSection, остальное — заглушки (сервисы читают только ключи).
/// </summary>
public sealed class FakeConfiguration : IConfiguration
{
    private readonly Dictionary<string, string> _values;

    public FakeConfiguration(params (string Key, string Value)[] pairs)
    {
        _values = pairs.ToDictionary(p => p.Key, p => p.Value);
    }

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var v) ? v : null;
        set => _values[key] = value!;
    }

    public IConfigurationSection GetSection(string key)
    {
        // Возвращает секцию-заглушку: indexer умеет только точные префиксы сборщика.
        var parent = this;
        return new FakeSection(parent, key);
    }

    public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();

    public IChangeToken GetReloadToken() => new FakeChangeToken();

    private sealed class FakeSection(IConfiguration parent, string path) : IConfigurationSection
    {
        public string? this[string key]
        {
            get => parent[path + ":" + key];
            set => parent[path + ":" + key] = value;
        }
        public string Key => path.Split(':').Last();
        public string Path => path;
        public string? Value { get => parent[path]; set => parent[path] = value; }
        public IConfigurationSection GetSection(string key) => new FakeSection(parent, path + ":" + key);
        public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();
        public IChangeToken GetReloadToken() => new FakeChangeToken();
    }

    private sealed class FakeChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new Dummy();
        private sealed class Dummy : IDisposable { public void Dispose() { } }
    }
}
