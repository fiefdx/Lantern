using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Lantern.Tests;

/// <summary>
/// WinForms controls must be created on an STA thread, which is not the default for
/// xUnit test workers. Wrap the body of a UI smoke test in <see cref="Run"/>.
/// </summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public static T Run<T>(Func<T> func)
    {
        T result = default!;

        // The cast is load bearing: `() => result = func()` is an assignment expression, so it is both an
        // Action and a Func<T>, and left to itself this method calls itself until the stack is gone.
        Run((Action)(() => result = func()));
        return result;
    }

    /// <summary>
    /// The same, for a body that also awaits: a window testing a real connection needs a thread that is
    /// an STA <em>and</em> a loop the continuation can come back to.
    /// </summary>
    /// <remarks>
    /// Without this, an awaited socket continuation resumes on a thread-pool thread and the next line
    /// touches the window from the wrong thread - which sometimes works, which is worse than failing.
    /// So the body's continuations are posted to <see cref="Pump"/>, and the pump runs in the wait loop.
    /// </remarks>
    public static Task RunAsync(Func<Task> action, int timeoutMs = 60000)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new PumpContext();

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(pump);

            Task task;

            try
            {
                task = action();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
                return;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                pump.Pump();
                Application.DoEvents();
                Thread.Sleep(5);
            }

            pump.Pump();

            if (!task.IsCompleted)
            {
                done.SetException(new TimeoutException($"the UI body never finished within {timeoutMs} ms"));
            }
            else if (task.IsFaulted)
            {
                done.SetException(task.Exception!.GetBaseException());
            }
            else if (task.IsCanceled)
            {
                done.SetCanceled();
            }
            else
            {
                done.SetResult();
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return done.Task;
    }

    /// <summary>
    /// Continuations wait in a queue and run when the test pumps, which is what keeps them on the thread
    /// that owns the windows.
    /// </summary>
    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();

        public override void Post(SendOrPostCallback callback, object? state) => queue.Add((callback, state));

        public void Pump()
        {
            while (queue.TryTake(out var item, 0))
            {
                item.Callback(item.State);
            }
        }
    }
}
