namespace Lantern.Core;

/// <summary>
/// Delivers an event so that a subscriber cannot break the object that raised it.
/// </summary>
/// <remarks>
/// <para>
/// Handlers are the windows, and windows fail: a form whose handle has just been destroyed throws
/// <see cref="InvalidOperationException"/> or <see cref="ObjectDisposedException"/> the moment it is
/// touched, and a handler with a bug in it throws whatever it likes. If that exception travels back up
/// the stack it does not end the window - it ends the frame loop of a connection, or worse, it escapes
/// <see cref="ChatServer.Join"/> halfway through a membership change.
/// </para>
/// <para>
/// The user then sees the worst possible failure: a chat that still says "connected" and receives
/// nothing. Losing the one message that was being delivered is the better outcome, and it is the
/// visible one, so it is reported through the status line where the user can act on it.
/// </para>
/// <para>
/// This is not a place to hide bugs: it wraps <em>delivery</em> only. The state change that a frame
/// causes is applied before any handler runs, so swallowing here cannot leave a room half-populated.
/// </para>
/// </remarks>
internal static class Dispatch
{
    /// <summary>
    /// Invokes <paramref name="deliver"/>, discarding an exception thrown by a subscriber.
    /// <paramref name="onHandlerFailure"/> is called with it so the caller can put something on the
    /// user's status line; it may be null.
    /// </summary>
    public static void Safe(Action deliver, Action<Exception>? onHandlerFailure = null)
    {
        try
        {
            deliver();
        }
        catch (Exception ex)
        {
            // Deliberately not rethrown. See the remarks: a window must not be able to stop a chat.
            try
            {
                onHandlerFailure?.Invoke(ex);
            }
            catch
            {
                // The thing that reports the failure is allowed to fail too, and that must not change
                // anything either.
            }
        }
    }
}
