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
        Run(() => result = func());
        return result;
    }
}
