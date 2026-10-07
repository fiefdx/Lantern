namespace Lantern.Tests;

/// <summary>
/// Waits for a condition across a live connection, and fails with a reason instead of hanging the run.
/// Tests that collect events use <see cref="Watch{T}"/>, which builds on this.
/// </summary>
public static class Watch
{
    /// <summary>Long enough for a loopback round trip, short enough that a real hang fails fast.</summary>
    public const int DefaultTimeoutMs = 8000;

    public static async Task UntilAsync(Func<bool> condition, string because, int timeoutMs = DefaultTimeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        throw new TimeoutException($"timed out after {timeoutMs} ms waiting for {because}");
    }
}

/// <summary>
/// Collects events as they arrive so a test can wait for one without blocking a thread (xUnit1031
/// forbids <c>GetAwaiter().GetResult()</c>, and a live connection delivers whenever it likes).
/// <para>
/// Subscribe first, act, then wait: nothing is missed, because every item that arrives is kept rather
/// than matched against a subscription that started too late.
/// </para>
/// <para>
/// Waiting on <see cref="Items"/> also sidesteps the trap of waiting on the <em>other</em> side's
/// state: the host applies a change before the frame telling this client about it arrives.
/// </para>
/// </summary>
public sealed class Watch<T>
{
    private readonly object sync = new();
    private readonly List<T> items = [];

    /// <summary>Everything collected so far, oldest first.</summary>
    public IReadOnlyList<T> Items
    {
        get
        {
            lock (sync)
            {
                return items.ToArray();
            }
        }
    }

    public void Add(T item)
    {
        lock (sync)
        {
            items.Add(item);
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            items.Clear();
        }
    }

    public int CountMatch(Func<T, bool> matches) => Items.Count(matches);

    public Task UntilAsync(Func<IReadOnlyList<T>, bool> condition, string because, int timeoutMs = Watch.DefaultTimeoutMs)
        => Watch.UntilAsync(() => condition(Items), because, timeoutMs);

    public async Task<T> ItemAsync(Func<T, bool> matches, string because, int timeoutMs = Watch.DefaultTimeoutMs)
    {
        await Watch.UntilAsync(() => Items.Any(matches), because, timeoutMs).ConfigureAwait(false);
        return Items.First(matches);
    }

    /// <summary>Appears in the timeout message, so a failing test says what did arrive.</summary>
    public override string ToString() => string.Join(", ", Items.Select(i => i?.ToString() ?? "null"));
}
