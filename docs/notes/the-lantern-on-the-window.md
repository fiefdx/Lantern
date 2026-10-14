# The lantern on the window

Asked for a picture: **"I add a icon_trans.png file under Media folder, please using it as our application's icon
shows on window title/top left, and system task bar app icon."**

It is a lantern with a Bluetooth flame inside it, a chat bubble at one side and a photograph at the other, which is
about as good a picture of this program as anybody could draw. Getting it onto a window is not a matter of handing
the picture to a window, though, because one picture has to be shown in four places and none of them ask for the
same thing.

## Four places, four appetites

| Where | What it asks for | When it can be changed |
|:--|:--|:--|
| Explorer's listing of `Lantern.exe`, and the taskbar jump list | a resource **inside the executable** | at build time, and not afterwards |
| A taskbar button | 24 px at 100% scale, more at larger scales | at run time, from the window's `Icon` |
| The corner of a title bar | 16 px | at run time, from the window's `Icon` |
| The alt-tab strip | 32 px or more | at run time, from the window's `Icon` |

So the picture is delivered twice. `<ApplicationIcon>Media\icon.ico</ApplicationIcon>` puts it inside the executable,
which is the only way Explorer and a jump list can be given anything; and `Media\icon.ico` is copied beside the
program as well, which is what `AppIcon` reads and hands to every window - the same reason the two sounds are files
next to the program rather than resources inside it (F10, see `Core/SoundNotifier.cs`): **a person can change the
picture without changing the code.** The two are the same bytes, written by one command, so they cannot disagree.

## Why a generated file, and why seven pictures inside it

The art is 1254 × 1254. Windows asks for a size and takes the closest frame it is offered, so an icon with only one
frame gets that frame shrunk by whoever happens to be asked, at the moment of drawing, by whomever Windows felt like
calling. Measured on this art, resampled here with Lanczos so the numbers are at least honest about being good:

| Frame | Transparent pixels | Pixels of flame |
|:--|:--|:--|
| 16 × 16 | 47% | **7 out of 256** |
| 24 × 24 | 49% | 17 |
| 32 × 32 | 51% | 34 |
| 48 × 48 | 53% | 87 |
| 256 × 256 | 55% | 2,801 |

Seven pixels of flame in a title bar is the honest limit of a drawing this crowded, and it is why the frames are
made here rather than on the way past: at least somebody chose them, and the tool says so out loud.

`tools/make_icon.py` writes `src/Lantern/Media/icon.ico` - 16, 24, 32, 48, 64, 128 and 256 pixels, **smallest
first**, because `new Icon(path)` in .NET reads the *first* entry in the file and that is what a window is handed
before Windows swaps in a better frame for the size it is drawing. Frames in the wrong order and every window starts
out holding a 256-pixel picture in a 16-pixel space.

That the binary is committed rather than built is deliberate: the compiler wants `Media\icon.ico` to exist before
anything else happens, and a blob in a repository with no way to remake it is a blob nobody dares change. The tool
is the provenance, and the art it reads - `Media\icon_trans.png` - stays in the repository beside it.

## The `trans` in the file name is the whole request

`Media` holds two versions of the drawing: `icon.png`, with its background painted in, and `icon_trans.png`, with
the background cut out. Only the second one is used, and there is a test about it, because a filled square in the
corner of a title bar reads as a window with a hole punched in the theme - and it is exactly the sort of thing that
gets "fixed" later by whoever happens to pick the wrong file. `AppIconTests` measures the transparency of every icon
it looks at and refuses anything less than 15% empty where this art is about half empty.

## What each window does, and what happens when the file is not there

`AppIcon.Apply(this)` is called once per window, in the constructor path each shares with its design-surface twin -
`ChatForm.SetUp` (which covers `HostForm` and `ClientForm` too), `StartupForm.SetUp`, `PictureViewerForm.SetUp`,
`ConfirmDialog.Present`, `NewRoomDialog.Present`, and `EmojiPickerForm`'s own constructor.

It is said out loud in code rather than left to the framework. Left to itself a window shows whatever the executable
carries, which is the same picture and looks identical - but a window that was never *told* what it shows is a
window no test can ask, and that difference is how "the icon is wrong" becomes an afternoon.

There is one icon object, shared by all six windows, and it is never disposed: a `Form` does not own the icon it
paints in its corner, so closing a window must not take the picture away from the windows still open.

And the file may be missing, which is the normal state of the Visual Studio design surface - it runs somewhere else
entirely and has no Media folder of ours to read. `AppIcon.Current` is then `null`, `Apply` does nothing, and the
window opens with the framework's own icon. **A window whose picture cannot be read is still a window.**

`ConfirmDialog` is not among the windows the tests check, because its only public constructor belongs to the design
surface and the program refuses to build a confirmation with no question to ask; it is shown modally by a window
that does wear the icon, which is where it gets one from.

## This is the one binary image in the project, on purpose

The house rule has been that pictures are drawn in code (`UI/ToolIcons.cs`), because toolbar glyphs have to follow
the font size and the theme, and a committed PNG of a bold "B" is a bold "B" that goes blurry when somebody makes
their text larger. That rule is about glyphs. An application icon is artwork - a lantern with gradients in it - and
nobody is drawing that in GDI at sixteen pixels.

## Changing it

Replace `src/Lantern/Media/icon_trans.png` (square, transparent background), then:

```
python tools/make_icon.py
```

then rebuild. The tool refuses art that is not square, and refuses to finish if a frame did not get written.

## The loose end, said plainly

Sixteen pixels of this drawing is a blue shape with a warm smudge in it. The chat bubble, the photograph and the
broadcast arcs merge into the lantern's edge. It is recognisable as *this* picture and it is not readable as a
picture - which is true of every detailed logo ever put in a title bar, but it is worth saying rather than
discovering. If a sharper corner is wanted, what is needed is a simplified drawing for the small frames: a second
source art file, which `make_icon.py` would use for its 16 and 24 pixel frames. Nothing here pretends otherwise, and
the tests deliberately do not claim anything is legible at 16 pixels - only that the frame is a real frame of the
right size and is not a blank.

## Proved

* Seven tests in `AppIconTests`: every openable window wears a lantern (flame present, mostly transparent); the
  executable offers an icon at 16, 24, 32 and 48 pixels and each one comes back that size rather than something
  scaled on the way; the executable's own 32-pixel icon is the lantern; and the picture arrives beside the program
  where `AppIcon` can read it.
* Run for real and looked at: `Lantern.exe` started, its window photographed - the lantern is in the corner of the
  title bar - and the taskbar's button for the running program is the lantern too.

Related: `docs/notes/the-face-is-the-button.md` (the other thing drawn in this program that is not a control),
`docs/notes/the-right-third-of-a-photograph.md`, and `UI/AppIcon.cs`.
