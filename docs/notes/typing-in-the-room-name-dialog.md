# The room-name dialog never re-checked the name (v1.1.1)

Reported the day after the room menu shipped:

> new room feature even input a room name, the ok button still gray out

It was not the menu's doing. The menu gave the naming dialog more traffic than it had had since T0.6, and the
dialog turned out to have never worked with a keyboard-and-mouse person in front of it.

## What was wrong

`NewRoomDialog` decides whether a name is usable in `ValidateInput()`, and sets two things from the answer: the
red line under the box, and `btnOk.Enabled`. It called that method from its constructor — when the box was empty,
so the answer was "Room name is required." and OK greyed — and from the `RoomNameText` property.

Nothing called it when a person typed. There was no `TextChanged` handler; `git log -S"TextChanged"` finds no
handler anywhere in the UI at any commit, so the dialog has been like this since the day it was added. The typing
went into the box, the rules were never asked again, and the button stayed greyed under `Study group` forever.

The half of it that *did* work is why it survived: Enter goes through `TxtRoomName_KeyDown` → `Accept()`, which
validates every time. So somebody at a keyboard could name a room and never learn that the mouse was dead. A bug
that lives on one of two paths to the same result, when the person most likely to notice is the one using the
other path, can live for years.

**The fix is one line of wiring** (`txtRoomName.TextChanged += TxtRoomName_TextChanged;` in the designer, and a
handler that calls the rules). Everything else on this page is about why 492 tests did not see it, because that is
the part that will bite again.

## Why nothing saw it

* **Every dialog test set `RoomNameText`, and that property validates on the way in.**
  `dialog.RoomNameText = "Study group"; Assert.True(Ok(dialog).Enabled);` passes with the wiring missing, because
  the seam through which the test supplies the name *also performs the behaviour under test*. A property that
  validates when you set it is a fine thing for the program to have; it is a useless probe for whether typing
  validates. The tests now type: one character at a time into the text box, found as a control, through
  `SelectedText` — which is the input path, and does not validate anything on the tester's behalf.
* **The host-side tests never opened a dialog at all.** `HostForm.AskForRoomName` is a `protected virtual` seam —
  deliberately, so window tests can answer a question without a person — and `AnswerableHost` overrides it. Right
  decision for those tests; the consequence is that no test in the suite had ever *shown* this window, so the
  dialog was only ever covered by tests that bypassed its one interesting behaviour.
* **Three documents claimed the opposite, calmly.** `HostForm`'s own comment said duplicate names are refused "by
  the dialog as they are typed, not after the OK click", the naming dialog's summary says it "refuses to close
  while the name is unusable", and a manual-checklist step said to expect refusal "as you type". A sentence is not
  a test. The README now says what happens and points at this note, and the checklist step says what to press —
  including the Enter key, precisely so the two paths cannot drift apart again unnoticed.

## The second hole, which the first one led to

While testing the fix: `btnOk.DialogResult = DialogResult.OK` was set in the designer. A button carrying a
`DialogResult` has the *framework* hand the form that answer once the click handler has run, happy or not. So the
dialog's central promise — it will not close while the name is unusable — was the framework's promise to keep, and
the framework has never heard of room names. Reachable by hand: type a name, and have it stop being usable between
the last keystroke and the click (another window taking that same room). `Accept()` had refused; the button had
already answered.

The button carries no answer of its own now. `Accept()` is the only thing in the dialog allowed to answer, and it
answers after asking. `btnCancel` keeps `DialogResult.Cancel`, which is correct and normal: "I take it back" needs
no vetting.

## Three things this harness cannot do, written down so the next person does not assume them

These were found with a throwaway probe test, and they are properties of testing WinForms without ever showing
it, not of this dialog:

* **`Button.PerformClick()` does nothing at all** unless the button `CanSelect`, and a control on a window that has
  never been drawn cannot be selected. A test that "clicks" a button on an unshown form and asserts the result is
  asserting whatever the fields already held. So there is no click-driven test here; `TheOkButtonCarriesNoAnswerOf-
  ItsOwn` asserts the *setting* the guard rests on instead, and says so.
* **`MaxLength` guards `WM_CHAR` — a real keystroke — not `SelectedText`.** Typing 30 characters into a 24-character
  box through the selection puts 30 characters in the box, with or without a handle. So
  `TheBoxWillNotLetANameBeTypedTooLong` asserts that the box is given the rule's own ceiling (24 = 24), which is
  the thing Lantern controls and the thing that would silently drift if somebody widened the box.
* **Assigning `Text` is a paste, not typing.** It bypasses nothing important here, but it is one event instead of
  several and it ignores the input limit, so the helper inserts character by character: the last keystroke is often
  the one that matters — a duplicate name becomes usable again when the last letter stops it being a duplicate.

## The rule, for the next person

In `CONTRIBUTING.md`, under code conventions: a control that answers what somebody types has its wiring in the
designer **and** a test that types into it; and no `DialogResult` on a button whose click handler can refuse.

Fixed in `fix(ui): the room-name dialog asks about the name every time it changes`, tagged `v1.1.1`. Four new
tests in `NewRoomDialogTests` (`TypingANameMakesTheOkButtonClickable`, `TypingTheNameBackOutGreysTheOkButtonAgain`,
`ADuplicateIsRefusedWhileTheDuplicateIsStillInTheBox`, `TheOkButtonCarriesNoAnswerOfItsOwn`) plus
`TheBoxWillNotLetANameBeTypedTooLong`; 497 in the suite.
