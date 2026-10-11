# The photo nobody could see

Reported after v1.2.1: *double-clicking an image opens the viewer, but the viewer does not show the image.*

It was true, and it had always been true.

## What was wrong

```csharp
image = picture.ToImage();
picImage.Image = image;      // this line was not there
```

`PictureViewerForm` decoded the photo, fitted it, scaled it, centred it, moved it, read its dimensions into the
status bar, put its percentage in the toolbar, disabled the zoom buttons when there was nothing to zoom, and
never once gave the pixels to the control that paints them. `git log -S "picImage.Image" --all` finds nothing in
any commit: the assignment has not existed at any point in the project's history. Every version of Lantern's
picture viewer has been a well-behaved dark grey rectangle.

Two lines fix it — the assignment, and clearing the control before disposing the image on close, because a
`PictureBox` holding a disposed image throws on the next paint and a closing window may paint once more on the way
out.

## Why nobody noticed, including the suite

Three things, and they are the same three every time.

**A seam that performs the behaviour under test cannot probe it.** Every transcript test that "opens" a picture
uses a subclass whose `OpenPicture` is

```csharp
protected override void OpenPicture(PictureMessage picture) => Opened.Add(picture);
```

which is exactly right for asking *which* picture a click meant, and is also a method that opens no window. So
`PictureTranscriptUiTests` had eighteen green tests about opening pictures, none of which had ever produced a
picture to look at. This is v1.1.1's room-name dialog again — a property that validates on the way in and a
subclass that answers instead of acting.

**Everything asserted was one step away from the screen.** The viewer's tests check that the scale reaches the
control's `Width`, that `FitScale` never enlarges, that the toolbar reads `100%`, that saving writes the bytes it
was given. All of that reads `image` — the field — which was populated correctly the whole time. Nothing read
`picImage.Image`, which is the only thing the screen looks at.

**The one check that would have caught it was the one that never got run.** Step 8 of the v1.1 sheet is *double-click
a photo; it should fill the window*. Hardware sign-off for v1.1 is outstanding — the two-laptop column is empty —
so the only person who would have said "there's nothing in there" was the person who had not run it yet. The bug
surfaced the moment somebody actually looked, which was today, on a machine with no Bluetooth dongle in it.

## What is tested now

`PictureOnScreenTests`, four cases, all of which fail when the line is removed (checked, not assumed):

- **`ThePictureIsGivenToTheControlThatPaintsIt`** — `pic.Image is not null`, with the sent dimensions.
- **`LookingAtTheWindowShowsThePhotoRatherThanAnEmptyPanel`** — `DrawToBitmap` the window, take the pixel in the
  middle of where the photo should be, require the red that was sent. The panel behind it is `#2D2D30` on purpose,
  so the failure message says `it was #2D2D30`, which is the empty panel telling on itself.
- **`ADoubleClickInTheTranscriptOpensAWindowWithThePhotoInIt`** — a real `HostForm`, a real photo, the double-click
  raised on the real `RichTextBox` the way the framework raises it, and then an owned `PictureViewerForm` in
  `OwnedForms` with a picture on it. Not the `OpenPicture` override.
- **`ADoubleClickOnALineThatIsNotAPictureOpensNothing`** — the same probe with nothing to open.

Raising that event is worth a word: a posted `WM_LBUTTONDBLCLK` does **not** make WinForms raise
`MouseDoubleClick` — it wants real input, which was checked before the test was written, and a test that silently
raises nothing looks identical to a program that silently opens nothing. `Control.OnMouseDoubleClick` by
reflection is the honest way to reach the last metre without driving the physical mouse across the developer's
desktop.

## The rule this pays for

CONTRIBUTING already says a feature the suite cannot perceive gets measured or gets looked at, and already says
the last metre of an affordance gets one test. This is the third defect in that gap and the first to survive two
milestones: a dialog's OK button no test had clicked, a context menu no test had opened, a photo no test had seen.
The new clause: **a test that overrides the thing under test and records the call is not evidence that it
happened** — which is fine as long as something else in the suite does the real thing once, in a real window,
with the pixels checked.
