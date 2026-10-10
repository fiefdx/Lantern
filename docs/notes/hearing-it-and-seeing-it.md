# Hearing it and seeing it: the grey chat area and the inaudible cue (v1.1.2)

One report, two defects, nothing in common except how they survived:

> message notification sound not working, and chatting area background should be white

504 tests pass with both of them in. That is the interesting part, so it is what this note is about — the fixes are
three lines and a sound file each.

## First, look at the window

Before changing anything, a throwaway test built a real `HostForm`, `Show()`ed it, called `DrawToBitmap`, saved a
PNG — and the PNG was **looked at**. It is the only honest way to answer "what does this look like to a person",
and it takes about a minute (the recipe is in `CONTRIBUTING.md`).

It showed the chatting area painted **#F0F0F0 grey** while the room list, the member list and the message box were
white. Four surfaces in one window, three of them alike, and the odd one out is the one everybody stares at.

### Why grey

A **read-only** text area paints itself with `SystemColors.Control` — the window's grey — not
`SystemColors.Window` like its editable neighbours. `rtbChat` had no `BackColor`, so it took the grey; the lists
and the box took white. Not dark mode (this machine has `AppsUseLightTheme=1`), not a theme, not high contrast:
the default of a read-only control, six inches from three controls that disagree with it.

### The fix, and the trap next to it

Pin the surface, and pin the ink:

```csharp
rtbChat.BackColor = Color.White;      // in the designer, where the layout lives
rtbChat.ForeColor = Color.Black;
TranscriptStyle.BackgroundColor = Color.White;
TranscriptStyle.ContentColor     = Color.Black;   // was SystemColors.WindowText
```

`ContentColor` used to be `SystemColors.WindowText`, and its comment explained that this kept the transcript
"readable on any background". That reasoning was sound only while the ink and the paper followed the *same*
system colour — and a read-only `RichTextBox` follows a different one. Pin the paper without pinning the ink and a
high-contrast machine hands you white on white. So the surface is `TranscriptStyle.BackgroundColor` and every ink
is chosen against it, in the same file, and `TranscriptLayoutTests` measures the pairs instead of trusting the
intent.

Measuring them found a second thing: the join/leave notice's grey, chosen to be *quieter than a message*, was
**4.3:1** against white — below the 4.5:1 that reading costs. It is `#6E6E6E` now. "Quiet" and "unreadable at the
back of the lab" were the same colour.

One more old trap, met again: `SystemColors.Window` and `Color.White` are one colour wearing two names, so the new
asserts compare `ToArgb()`. A test that can tell those apart is testing the CLR's colour book, not what anybody sees.

## The cue that was too quiet to hear

The wiring was checked first, because "no sound" usually means the plumbing: the wav files *were* in the output
next to the exe; `AttachSounds` is on the one funnel both roles go through (`StartupForm.Open`); the cue is raised
off `MessageReceived`; and a probe that called `SoundPlayer.Load()` and `PlaySync()` played **both files without
complaint** on this machine.

What was wrong was the sound itself:

| | duration | peak | rms |
|---|---|---|---|
| `message.wav`, before | 0.12 s | −11.7 dBFS | −18.8 dBFS |
| `file-ready.wav`, before | 0.34 s | −11.7 dBFS | −18.8 dBFS |
| `message.wav`, now | 0.35 s | −2.5 dBFS | −14.6 dBFS |
| `file-ready.wav`, now | 0.60 s | −2.5 dBFS | −14.6 dBFS |

A twelfth of a second at a fifth of full scale is a tick. On a laptop speaker, in a room with four people talking,
it is not a cue. Nobody noticed because the sound suite — 35 tests — **counts cues**: `SoundCue.Message` arrived at
the notifier, therefore F10 "works". Nothing in 504 tests has ears.

The cues are now a rising two-note for a message and a longer rising three-note, in a lower register, for a file
that has arrived — the same two reasons F10 asks for two sounds rather than one: *somebody spoke* and *the file is
here* are different reasons to look up. Format is uncompressed 16-bit PCM, because that is the only thing
`SoundPlayer` will play; ADPCM or float throws, and until this change that throw vanished without trace.

They are generated, not recorded — `tools/make-sounds.py`, in the repo, byte-for-byte reproducible — because a
feature whose signal has no source is a feature nobody can re-make on purpose.

And the files are now measured: `TheTwoSoundsAreLoudEnoughAndLongEnoughToHear` reads the RIFF header (format tag,
bits, channels), the duration and the loudest sample, and refuses a cue shorter than 0.2 s or quieter than half of
full scale, and refuses two cues of the same length. That is what a suite without ears can do about loudness:
measure the thing, rather than trust the intention.

## When it is still silent, the window says so

Every failure in `SoundNotifier` is swallowed on purpose — a chat message must never fail because of a sound card —
which is right for the message and hopeless for the person sitting in front of "it doesn't work". So the first
reason is kept and said once:

```csharp
public string? WhySilent { get; private set; }      // "message.wav is not next to Lantern"
public event Action<string>? CouldNotPlay;          // and ChatForm puts it in the bar: "no sound: …"
```

`SoundDirectory` became a settable knob (as `Muted` already was) so the missing-file case can be tested against an
empty folder instead of hiding the real sounds from the rest of the suite.

**The first thing to check, before any of this**: only the window that *receives* makes a noise. Your own words are
silent by design ("you do not see your own words twice" applies to hearing them too), so two windows on one
machine with one of them sending means one window is quiet and behaving correctly. That belongs in the manual sheet
precisely because it looks like a fault.

## What the two had in common

Both defects were invisible to the suite because the tests perceived the program through its own abstractions: a
cue is a value of an enum, a colour is a symbol in a class. Neither is what a person meets — a person meets a
pressure level in a room and a colour on a surface. Hence the rule now in `CONTRIBUTING.md`: *a feature the suite
cannot perceive gets measured, or gets looked at* — contrast ratios and sample peaks instead of "it is blue" and
"it is loud enough", and a screenshot you actually open rather than a description of one.

Fixed in `fix(ui): the chat area is a white surface, and the cues can be heard`, tagged `v1.1.2`. New tests:
`TheTranscriptIsWrittenOnWhiteLikeTheRestOfTheWindow`, `EveryColourInTheTranscriptCanBeReadOnIt`,
`ASoundThatIsNotThereIsSaidOutLoudOnce`, `ASoundTheMachineCannotMakeIsSaidOutLoudOnce`,
`ANotifierThatCanBeNullSaysNothingAboutIt`, `AWindowThatCannotBeHeardSaysSoInItsBar`,
`TheTwoSoundsAreLoudEnoughAndLongEnoughToHear`. 504 tests, twice, 0 warnings.
