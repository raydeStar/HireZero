namespace HireZero.Core;

// Scoped continuations carry the request lock to the exact point where a command or HTTP request starts.
public static class AgentEffectBoundary
{
    private static readonly AsyncLocal<Action<Action>?> Current = new();
    public static IDisposable Enter(Action<Action> guard)
    {
        var previous = Current.Value; Current.Value = guard; return new Scope(() => Current.Value = previous);
    }
    public static T Start<T>(Func<T> action)
    {
        if (Current.Value is not { } guard) return action();
        T result = default!; guard(() => result = action()); return result;
    }
    private sealed class Scope(Action release) : IDisposable { public void Dispose() => release(); }
}
