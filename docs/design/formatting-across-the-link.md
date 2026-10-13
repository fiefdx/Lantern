# Formatting that travels: the design before the code

*v1.7 (planned). Decided: "I send bold words, others see bold words."*

Nothing here is built yet. It is written down because the decision changes the wire protocol, and a protocol change
done halfway is the one mistake a peer-to-peer program cannot recover from gracefully — two machines in a room end up
unable to read each other, and the person in the middle has no idea which one is at fault.

## The rule the whole design hangs on

**The sentence is never optional. Formatting is an annotation on it.**

`TextMessage.Body` stays exactly what it is today: plain text, counted by v1.4's ruler, capped at 500 characters, and
always present and always enough to read the message. A style list is bolted on beside it and can be thrown away by
any receiver — old, new, confused, or simply unable to parse it — without anybody losing a word. That single rule is
what makes this safe to roll out across machines that are not on the same version.

It also answers the compatibility question without a version handshake: a v1.6 peer deserialising a v1.7 header
ignores the field it doesn't know and shows plain text. A v1.7 peer receiving from v1.6 gets no style list and shows
plain text. Nobody is ever told their chat is out of date.

## On the wire

Text already rides in the frame's JSON header (`Frame.JsonHeader`, UTF-8 bytes); `Body` is for binary payloads and is
not involved. So this is a header change, not a new frame type.

```json
{ "kind": "text", "sender": "Haitao", "room": "Room 1", "body": "see the bold word",
  "styles": [ { "n": 8 }, { "n": 4, "b": true }, { "n": 5 } ] }
```

Keys are the field names as this document draws them; on the wire they are the header's own (`text`, `nick`,
`styles`). The example above first added up to 19 over a 17-character sentence, which is the one thing the
third bullet below says a receiver must throw away, and it wrote the bold flag as `1` where the encoder writes
a JSON `true` — both corrected here rather than in the code, because the rule and the encoder are both right.

* **Run-length, not per-character, and not offsets.** A run is `{ n }` for plain text, `{ n, ...style }` for styled
  text, laid end to end over the whole body. No start indices means no overlapping spans, no gap, and no ordering
  problem for a receiver to get wrong.
* **`n` counts text elements** — the same ruler `ValidationRules.Length` uses since v1.4, and the same thing a
  person counts. Offsets in UTF-16 units would split a face in half; a run boundary inside `👨‍👩‍👧` would give half a
  family in bold and half not.
* **The sum must match.** If the runs don't add up to `Length(body)`, the receiver **ignores the whole style list**
  and shows the plain text. A style list that disagrees with the text is a bug in the sender, and the recipient has
  no way to guess which half to believe. This is the only failure mode, and its remedy is always the same: read the
  sentence.
* **At most 64 runs** per message. More than that is either nonsense or somebody trying to make a GDI control spend
  its evening laying out runs.

Style fields, all optional and additive:

| key | meaning | receiver with no idea what it is |
| --- | --- | --- |
| `b` | bold | ignored, run shown plain |
| `i` | italic | ignored |
| `u` | underline | ignored |
| `c` | colour, `#RRGGBB` from a fixed small palette | ignored |
| `s` | size, one of `0` / `1` / `2` (small, normal, large) | ignored |

Bold, italic and underline are **independent flags on a run**, not a font-style enum: the wire carries three bits and
the receiver composes whatever font it has. That matters on the way out too - see the toggles below.

Italic was left out of the first draft on the grounds that nobody asked for it. It was asked for, so it is in, and the
design notes the cost honestly: a third style flag doubles the combinations the run tests have to cover (bold, italic,
underline, and all three together on one run) and adds the RichTextBox toggle trap below.

`Body` is unchanged on the wire with or without styles, byte for byte — which is the assertion that proves a v1.6
peer is not being subtly broken.

## In the domain

`TextMessage` gains an optional read-only list of styled runs, defaulting to empty. It stays a record and every
existing construction keeps compiling — the ~200 places that build a `TextMessage(sender, room, body)` are the reason
this is an optional extra and not a constructor parameter.

The style list is validated **where it is accepted from the wire**, not in the domain object's constructor: a message
with an impossible style list must still arrive as a message.

## In the transcript

`ChatForm.Append` already writes RTF per entry and already interleaves inline pictures (photos v1.2, file pages v1.3,
faces v1.5), so writing a run with `\b`, `\ul`, `\fs<half-points>` and `\cf<colour>` in its own group is the same
machinery pointed at formatting.

* Headlines stay unformatted. A bold word inside a bold-ish headline is noise, and the headline is metadata.
* A face inside a styled run stays a picture — the run's formatting wraps around the `\pict`, which RTF nests happily.
* Copying the transcript out still loses pictures (v1.5) and now also loses style. That is what a RichTextBox copy
  is; the message itself is intact, and it is in the room, and that is what the other machine received.
* Hit-testing from v1.3.1 walks character positions in the control. Formatting adds no characters, so the file and
  picture spans are unaffected — but there must be a test that says so, because "unaffected" is exactly the sort of
  claim this project has been wrong about before.

## In the input area — the expensive part

Bold in a box requires a box that can hold bold. `txtMessage` is a classic edit control: one font, one colour, no
runs. It becomes a `RichTextBox`, and that swap is where the work and the risk are:

* **`MaxLength` stops existing.** An edit control refuses keys past the limit; a RichTextBox does not have the
  notion. Counting moves into `TextChanged`, in text elements, with the v1.6 lesson attached — assignments bypass the
  control's own limits, and any trimming done inside `TextChanged` re-enters it, so it needs a re-entry guard and a
  message in the status bar rather than a silent chop.
* **Enter sends, Shift+Enter breaks a line.** A RichTextBox takes Enter for itself; the existing `KeyDown` handler
  needs `e.SuppressKeyPress`, and both halves get a test.
* **Pasting becomes a decision: paste as plain text.** A selection copied out of a browser arrives with 11 kinds of
  formatting and a background colour. The box is for a chat message; what it accepts is the text.
  *Caught while building it:* the obvious way to do that is to intercept the window's paste message, and the paste
  message does not mean what its number says. The RichEdit window behind a RichTextBox is sent three messages with
  the paste message number while it is being created - before the form has finished showing, nothing focused,
  nobody at the keyboard. Honouring them meant that entering a room pasted whatever was sitting on the clipboard
  into the message box, silently, and the first anyone heard of it was three tests that had nothing to do with
  pasting. A paste is intercepted on the keystroke, and the message is honoured only on a box that has the focus
  with a modifier held down. There is a test named *a window opening pastes nothing*.
* **Toggles apply to the selection** (`SelectionFont`, `SelectionColor`, `SelectionCharOffset`), and to what is typed
  next. They reset when the message is sent. Style state that outlives the send is how a chat turns into a
  ransom note.
* **The RichTextBox style mask is a trap, and it is exactly the trap italic walks into.** Assigning
  `SelectionFont = new Font(family, size, FontStyle.Bold)` replaces the *whole* style mask, so a naive Bold button
  silently removes italic from the selection - and an Italic button silently removes bold. Each toggle has to read
  the selection's current style, set or clear its own bit, and write the composition back; and a toggle pressed on a
  partly-styled selection applies to the whole selection, with the button shown pressed only when **every** run in it
  already has the style. Two tests live here: *bold then italic leaves both on*, and *italic then the same italic
  button leaves bold alone*.
* **The toolbar goes above the box**, which is also the two-line box that was asked for: Bold, Italic, Underline,
  Size, Colour, Emoji, Share — Send stays where it is, because muscle memory around Send is worth more than
  symmetry, and it stays *off the strip* because it ends a message rather than putting something into one.
* **Size and colour are menus; bold, italic and underline stay toggles.** The first two used to cycle, which hid
  five of the six colours behind a click nobody thought to make and made the current size invisible until a person
  clicked round through all three. A menu is the whole list at once, with a tick on the choice the caret is sitting
  in, and it is built from the same lists the composer applies, so what is on offer cannot disagree with what is
  sent. The three that switch on and off stay one click.
* **Two tools wear a shape instead of a word**: Windows' own smile for the emoji table, and a sheet of paper drawn
  in code for sharing a file. The words did not go away — they are the accessible name and the tooltip, which are
  the two ways of asking that a picture cannot answer. Both pictures are sized against the button that shows them,
  because WinForms neither scales a button's image nor grows the button: it draws what it is given and cuts the
  rest, silently. Measuring why the face looked cut found something worse than a sizing mistake — see
  `docs/notes/the-face-is-the-button.md`.
* **Roughly 30 tests read `Box(form)` as a `TextBox`** and several wireframe-parity tests assert the geometry of the
  input row. They all move together with the control, and that is fine — it is a slow change, not a dangerous one.
  What would be dangerous is the first two bullets, done quietly.

**The colourful faces in the box, and what measuring it changed.** This section used to refuse the request: once the
box is a RichTextBox it *could* draw faces in colour like the transcript, and it was argued that it should not,
because a picture inside the box is not a character, so the 500-character count would silently stop counting the
faces and the wire would have to map pictures back to the emoji they came from. That refusal was overruled by the
person using the product, and going to measure why it had been written produced something worse than the concern it
recorded:

* A `\pict` group in an *editable* RichTextBox is not a U+FFFC object replacement character, which is what every
  account of this gives. It is a **space** - `0x20`, one ASCII space, in `.Text`. Type `a`, insert a picture of a
  face, type `b`, and the box contains `a b`. The face is not merely uncounted, it is *gone*: the sentence that
  reaches the room says "a b", and the person who typed `a🙂b` has had a word taken out of it.
* Hidden text (`\v`) survives in `.Text` and takes up no width, so it is tempting as a way to keep the character
  somewhere. It is not: it is text, so the count and the wire see it, and RichEdit mangles any character that is not
  escaped as `\uXXXX\uXXXX` - the faces came out as `??`.

So the rule the refusal was protecting is right, and the box has to hold the *character*: faces are counted, sent and
compared as the emoji they are. Which leaves colour as paint and nothing else - the glyph drawn over the character
the box already contains, in the box's own background colour underneath it, so that what is on the screen is a
picture and what is in the control is still one character. That is the route left standing, and it is the one this
feature takes. Text stays the truth; colour is what the window does with it.

**What the four measurements said** (`FaceOverlay`, and every number below was taken off the screen rather than
argued from documentation, because the documentation about this control turned out to be wrong twice):

* **`OnPaint` does not happen.** The paint message arrives at a RichTextBox's window procedure and WinForms invokes
  neither the override nor the `Paint` event - counted: one paint message in, zero `OnPaint` calls. So the only place
  anything can be painted on this control is inside `WndProc` after the base has handled `WM_PAINT`. The placeholder
  word from T12.3 had been drawn in an `OnPaint` override this whole time, which means **the box never once said
  "Message" on screen**, and its test kept passing because it asked the `ShowsHint` property, which is a fact about
  the text and not about the window.
* **A control that cannot be photographed can still be photographed off the display.** `DrawToBitmap` on a RichEdit
  returns a picture with almost none of its glyphs in it (31 dark pixels for a line of text), which is why nothing
  here is asserted from a screenshot of the control. `CopyFromScreen` at the control's screen position does work -
  but not immediately: pixels read straight after a repaint are mid-composition and lie (the same white background
  read `D7D7D9`, then `E7E7E8`, then `ECECED` on three successive captures). Settle the window first. Two more ways
  the same measurement goes wrong, both of them found the hard way: an active RichTextBox has a blinking caret in the
  capture, and a caret is a column of ink one line tall; and a window sitting partly below the bottom of the screen
  captures as black, which reads as "no ink anywhere" once the paper sample is taken from inside it.
* **The glyph is the advance. The picture of a face is not the same square as the face.** Asked where a face is, a
  RichTextBox answers with where its *advance* begins - 12 pixels wide at 9pt - and measured cleanly the glyph's ink
  is that advance exactly: square, starting on the cell's own left edge, sitting a little below its top (two pixels
  at 7 and 9pt, three at 11 and 13pt). The first measurement here said 15 across on a 12 pixel advance, and the
  geometry built from it - `advance + advance / 3`, one pixel inside the cell - put the colour face up and left of
  the character with four pixels of black outline standing out on its right. The extra three pixels were a **full
  stop**: the string measured had been `🙂.` and the period was counted as part of the face. Measure a face with a
  run of spaces after it and the caret parked in the spaces, and the glyph is the advance.

  The other square is the artwork's, and it is not either of those: `ColorEmoji` rasterises a colour glyph with air
  round it - three pixels of it at nearly every size from 9 to 25 measured, so a 15 pixel bitmap inks 12 and a 22
  pixel one inks 18, but never the whole bitmap.
  So `CachedGlyph` reports the ink it measured while rasterising, and `DrawOf` hangs the drawing from the middle of
  that ink at the scale that puts the ink on the glyph - the only point on the picture that may be placed by the
  square. Ask for the bitmap at the square plus three and the scale lands near one, which is to say the face is
  rasterised at the size it is shown rather than stretched into it.

  The erase underneath the face is an **ellipse** one pixel larger than the square: the footprint to cover is round,
  antialiasing feathers about a pixel past the ink at every edge, and a rectangle would fill the corners where the
  tops of the neighbouring letters' strokes live. Verified by measuring two windows separately - an ordinary
  RichTextBox, and the message box - at 7, 9, 11 and 13 points: the painted face's ink occupies exactly the
  rectangle the control's own glyph occupies, and neither has any ink outside that square.
* **What is behind a selected face, and when nothing is.** A RichEdit window's selection is `SystemColors.Highlight`
  exactly (`FF0078D7` here, sampled). When the box loses the caret it draws **no selection at all** - the pixel where
  the blue was reads white again - so an unfocused selected face is patched with the paper, not with a blue nobody is
  looking at any more. That is why the control asks for a repaint when it gains and loses the focus as well as when
  the text, the caret, the size or the scroll move.

## Tasks

* **T12.1 [Core/Transport]** the style runs: DTO fields, encode/decode, the sum-must-match rule, the 64-run cap, the
  unchanged-`Body` rule, and a test that a header carrying an unknown field still decodes into the message it
  arrived as. Combinations get named tests rather than a theory with a boolean triple: `bold+italic+underline` on one
  run of one character, sent once and compared against what came out.
* **T12.2 [UI]** the transcript renders runs, and a formatted message round-trips through two real loopback sockets
  with the style intact.
* **T12.3 [UI]** toolbar above the input, two-line box, RichTextBox swap, counting in `TextChanged`, Enter and
  Shift+Enter, paste-as-plain, toggles that reset on send. `EmojiPickerForm` and Share move into the strip.
* **T12.4 [QA]** the existing 599 updated for the new input control, plus the new behaviour's own tests; the wireframe
  sheets re-drawn.
* **T12.5 [Docs]** README, checklist, note — and the two things left behind on v1.6: `EmojiPickerForm` and
  `ColorEmoji.Glyph` are still missing from `docs/umls/02-class-ui-and-services.puml`, and there is no long-form note
  for the emoji table.

## What to say if any of this is wrong

1. Runs keyed by counted characters rather than by offsets — if you would rather have offsets, say why.
2. Older peers silently showing plain text instead of being warned.
3. ~~Faces staying outline in the box for this round.~~ - asked for, now in: the character stays and the face is
   painted over it, for the reasons measured above.
4. ~~Italic~~ — asked for, now in (see the style-mask trap above).

## T12.1 work order (for the session that writes the code, not for reading)

Written so the whole session can be spent on the change instead of on finding it. Red first, then green, one commit.
Nothing in this list touches a control; if a `.Designer.cs` file is in the diff, the session has drifted.

`grep -rn "class ChatHeader" src/Lantern` finds the DTO the frame header serialises - it is the shape that gains the
`styles` field, and `grep -rn "TextMessage(" src/Lantern/Core` finds the two places that map between DTO and domain
(one per direction). Files, in the order to touch them:

1. **`src/Lantern/Domain/StyledRun.cs`** (new) - `public sealed record StyledRun(int Length, bool Bold, bool Italic,
   bool Underline, int Size, string? Color)`, plus `static IReadOnlyList<StyledRun> Plain(int length)`, plus
   `static bool Fits(IReadOnlyList<StyledRun> runs, string body)`: the sum of `Length` equals
   `ValidationRules.Length(body)`, no run has `Length <= 0`, and `Count <= 64`. `Size` is 0/1/2 and `Color` is
   `#RRGGBB` or null - both validated here, not trusted from the wire.
2. **`src/Lantern/Domain/TextMessage.cs`** - add `public IReadOnlyList<StyledRun> Styles { get; init; } =
   Array.Empty<StyledRun>();`. Not a constructor parameter: ~200 test call sites build `TextMessage(sender, room,
   body)` and they must keep compiling untouched. `Room.Append`/transcript code reads `Styles` and never writes it.
3. **The header DTO** - `List<StyleDto>? styles`, with `[JsonPropertyName("styles")]` and the run key named `n`, so
   the JSON on the wire is exactly the shape at the top of this document. Null means "no styles", which is what every
   pre-v1.7 message is.
4. **The two mapping sites** - domain to DTO: emit runs only when `Styles` is non-empty, and never emit an empty
   array (an empty array would fail the sum rule on the receiver and be discarded, which is correct but wasteful).
   DTO to domain: `Fits(runs, body)` or **discard the whole list**, never part of it; a discarded list must leave
   `Body` exactly as it arrived.
5. **`FakeChatService`** - it is the only ChatService the UI tests use, so a message sent through it must carry
   `Styles` from sender to transcript, or every UI test written later will be testing a service the wire does not
   have.

Tests, written before 1-5 exist so they are red for the right reason:

* `AStyledMessageStillCarriesTheSentenceByteForByte` - encode the same text with and without styles, assert the
  decoded `Body` is identical, and assert the unstyled header contains no `styles` token at all.
* `ARunThatDoesNotAddUpToTheMessageIsThrownAwayWhole` - runs summing to one short of the body: `Styles` empty, `Body`
  intact, and the sentence still readable. Same for one long, and for a run of length zero.
* `AFamilyEmojiIsOneCharacterOfStyleOrNone` - one run over `👨‍👩‍👧`: length 1 by the ruler, and it round-trips styled.
  This is the test that exists because runs are counted in characters rather than offsets.
* `SixtyFiveRunsOfStyleDoNotArrive` - cap at 64, message still delivered.
* `AHeaderNobodyRecognisesStillArrivesAsTheMessageItArrivedAs` - hand-written JSON header with an unknown field
  (`"hats": 3`) decodes into the right sender, room and body. This is the v1.6 peer's defence, and it is the one test
  here that protects users rather than code.
* `BoldItalicAndUnderlineOnOneCharacterComeBackAsAllThree` - named, not a theory over three booleans.

Run: `dotnet test Lantern.sln --nologo -p:OutputPath=bin/agent/ -m:1 -nodeReuse:false` - 0 warnings, and the count
goes up by the new tests only. Then commit as `T12.1 [core,transport] formatting rides beside the sentence, never
inside it`. T12.2 renders runs in the transcript; the box does not move until T12.3.
