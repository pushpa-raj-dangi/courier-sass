using Microsoft.Extensions.Options;

namespace Courier.Merchants.Tests;

// Minimal IOptionsMonitor<T> test double that always returns a fixed value — enough for unit
// tests that construct a token service directly (no DI container) and don't exercise live
// signing-key rotation, which is covered separately by the reload-specific tests.
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T currentValue) => CurrentValue = currentValue;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
