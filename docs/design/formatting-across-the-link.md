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
* **The toolbar goes above the box**, which is also the two-line box that was asked for: Bold, Underline, Size,
  Colour, Emoji, Share — Send stays where it is, because muscle memory around Send is worth more than symmetry.
* **Roughly 30 tests read `Box(form)` as a `TextBox`** and several wireframe-parity tests assert the geometry of the
  input row. They all move together with the control, and that is fine — it is a slow change, not a dangerous one.
  What would be dangerous is the first two bullets, done quietly.

**One thing deliberately not done in this round.** Once the box is a RichTextBox it *could* show colourful emoji
pictures, like the transcript. It will not: a picture inside the box is not a character, so the 500-character count
would silently stop counting the faces, and the wire would have to map pictures back to the emoji they came from.
Faces stay text in the box (outline), pictures stay in the transcript (colour). If that is the wrong call, say so —
it is the only decision here that was made for convenience rather than for the person using it.

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
3. Faces staying outline in the box for this round.
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
