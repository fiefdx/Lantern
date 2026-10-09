# Closing a chat window goes back, and does not stand still

Two reports after `v1.0.2`, both about the end of a chat:

> host close chat window should back to server create/scan window
>
> current close operation use too much time

They are the same knot. Closing the chat window *was* the program's exit, and that exit was written to be
certain the chat had stopped before the window let go of the thread it ran on — so the thing a person felt
was the program stopping, and it took as long as a Bluetooth channel takes to hang up.

## What was there

```csharp
// StartupForm.ShowChat
chat.FormClosed += (_, _) => Close();      // the chat window ends the start screen, so it ends the app
```

```csharp
// ChatForm.OnFormClosed
Task.Run(() => Service.ShutdownAsync()).Wait(TimeSpan.FromSeconds(15));
```

Neither line was careless. The second one has a comment explaining itself, and the explanation is F9:

> A shutdown started fire-and-forget on the way out has no reason to finish before the process goes, and
> the host's room list may still be on its way to the file — so a room created seconds earlier, or renamed,
> or deleted, quietly comes back.

That is true, and it is true of the *process* going away. What it was paying for was the *window* going
away, which is a different event and one the person has already left. On a loopback chat the difference is
invisible — measured here, 64 ms — and over RFCOMM it is not: closing an `RfcommConnection` and
unregistering the SDP record are the two calls the stack is entitled to take seconds on, and every one of
them was being spent with the UI thread parked inside a `Wait`, on a window that was already gone from the
screen.

## Waiting is a resource, so it got budgets

Nothing is fired and forgotten. The stopping is started and **handed over** (`ChatForm.StoppingChat`), and
each place that could wait now says how long it is willing to, out loud in the code:

| Who waits | For what | Budget | Why that number |
|---|---|---|---|
| the window being closed | the chat's shutdown | 500 ms | about how long a window that has already been told to close may be seen thinking before it looks broken |
| `BtRfcommServer.Stop()` | the SDP unregister and **every open channel, all at once** | 1 s | the goodbye is not cancelled by walking away, only no longer stood still for; three slow clients used to be three seconds in a row |
| `ChatServer.StopAsync()` | the accept loop coming back | 2 s | `BluetoothListener` has no cancellation token — stopping it is what breaks the wait, and Windows takes the listener down at its own speed |
| `ChatClient` hanging up | the link close, and the read loop noticing | 1 s each | the read loop is unblocked *by* the close; waiting longer is waiting on the stack |
| the start screen, before another chat | the last chat's packing up | 8 s, then it says so | a machine cannot advertise the same service twice at once — starting anyway would answer a second "Start a Server" with a failure that reads like broken Bluetooth rather than like an overlap |
| the start screen, on its way out | the same task | 5 s | after this window there is no message loop left, and the room list is owed to the file |

So F9 is still promised, in the place the promise belongs: the program's own exit waits for the write, and
a window's exit hands it over. `AWritePromisedOnTheWayOutIsFinishedBeforeTheProcessGoesAway` is the old
`ClosingTheHostWindowWritesWhatItPromisedBeforeTheProcessGoesAway` rewritten to that contract — it closes
the window, checks that the write is genuinely still running, and *then* does what the start screen does.

## What closing means now

Closing a chat window — either role, and by the ✕, Alt+F4 or File ▸ Quit alike — stops the chat and brings
the startup window back. The start screen's ✕ is the program's ✕.

* The host is still asked first, because everybody else is connected to it. The question now says where
  they end up: *"Stop the chat and go back? Everyone still connected will be told the host has gone."*
* A client is still not asked, because nobody else depends on it staying open.
* The start screen says why it came back. Leaving is `the chat is closed — host one or join one`; losing
  the host is `the chat ended: The host closed the connection.` — a start screen appearing in silence looks
  like a program that was never in a chat at all. That is `ChatForm.ClosingExplanation`, set by the
  connection-loss path and read once on the way back.
* The service and the server this window made are disposed once the chat has stopped. A chat window can be
  opened and closed repeatedly now, and a joined chat left undisposed is an RFCOMM channel left open, of
  which a radio has a small handful.
* `ITransportFactory` was already the seam the start screen builds its chat from, so nothing about the
  protocol changed. **Unlike v1.0.2, this build talks to v1.0.2 builds**: the wire is untouched.

`StartupForm` grew two small hooks — `NewHostWindow`/`NewClientWindow` — because which window to show for a
host and for a join is a decision the start screen makes, and a decision that cannot be replaced is a
decision that cannot be tested without a modal dialog in it.

## Tests

`ClosingTheChatWindowTests`, six: the host window's close brings the start screen back and the buttons work
again (and a second chat can be hosted on it); the client window's close does the same and the channel is
really closed; the start screen reports a lost host rather than arriving silently; **a window does not stand
waiting for a radio that is slow to let go** (a radio faked to want three seconds, `Close()` timed at well
under two, and the hang-up then checked to have finished anyway); starting a second chat waits for the first
to finish letting go rather than double-booking the service; and closing the *start screen* does not walk
away from a chat still packing up.

`RfcommTransportTests.StoppingARadioThatIsSlowToLetGoDoesNotHoldTheHostUp` puts the same question one layer
down, where the answer is the radio's own: a listener whose `Stop()` wants three seconds and channels that
each want three seconds more, all of them closed, the call returned in about one, and every channel
eventually closed — because giving up *waiting* is not giving up *closing*.

Suite: **437 green** (430 before), build has 0 warnings.

Two honest caveats. The three-second Bluetooth close is a fake's three seconds: this session has no paired
radios, so the numbers here are what `docs/testing/m4-field-test.md` step 4.6 measures on hardware, not
what was measured. And `Sta.Run<T>` had a latent infinite recursion in it — `() => result = func()` is an
assignment expression, which is both an `Action` and a `Func<T>`, so the generic overload was calling
itself — which these tests were the first to step on. Fixed with a cast and a comment, because the next
`Sta.Run<int>` would have found it again.

## The shape of the thing, said once

A window that has been closed is not the place to wait for slow work. The wait is usually legitimate — the
work has to finish before *something* — but it belongs to whoever cannot proceed without it (a new chat, an
exiting process), not to the last thing the person was looking at. A timeout on the way out is a guess about
how long the world takes; a handover plus a budget per waiter says which world each of them is waiting for.
