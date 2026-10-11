# Counting what somebody counts

*Written for v1.4, which added emoji support to what people type. The code it changed is
`Domain/ValidationRules.Length`; the tests are `EmojiTests`, `FrameCodecTests` and two scenarios in
`EndToEndScenarioTests`.*

## Three numbers for one face

Ask a C# program how long `😀` is and it says **2**. Ask Windows how many bytes it takes over a radio and it says
**4**. Ask JSON, which escapes it as `\ud83d\ude00`, and it says **12**. Ask a person and they say **one**.

Lantern had been measuring messages, nicknames and room names with the first of those. Nobody had complained,
because the only people who hit it would be people typing emoji — and until this release, nobody had ever typed one
into a test either. What they would have got is worth writing down:

* a message of **251 faces** refused as "must be 500 characters or fewer", to a person counting half as many as
  they were told;
* a nickname of **nine** faces refused as more than 16 characters.

A refusal whose number cannot be checked by the person reading it teaches them that the program is arbitrary. That
is a bug even when the arithmetic behind it is correct.

So the ruler is now `ValidationRules.Length`, which counts **text elements**: one emoji, one character — and, as a
bonus of doing it properly, `👨‍👩‍👧` (three people and two zero-width joiners) and `🇻🇳` (two regional indicators) are
one character each, because that is what somebody looking at them counts.

**What did NOT change**: the wire. A frame has always counted **bytes** of UTF-8, which is the only thing a radio
understands, and that was already right. `AnEmojiTravelsAsFourBytesNotTwoCharacters` exists because it is the
arithmetic that would have broken everything had it been the other way round: measure a frame in characters and the
far end reads half a message and calls what is left the beginning of the next one.

## Whether they were drawn was never measured

Nothing in Lantern rejected an emoji. They went into the box, into the room, onto the wire, into the transcript — and
the suite never once looked at what came out. The question was whether a **RichTextBox**, which is a GDI control
with no colour-font machinery of its own, puts a glyph there at all.

So it was measured rather than argued. The answer on this machine: yes, something is drawn where the emoji is, via
Windows' normal font fallback — `Segoe UI` in the transcript never needed changing, and setting the run to
`Segoe UI Emoji` explicitly makes no difference to a single pixel. That is worth knowing, because "add emoji
support" suggests a feature and what was needed was a ruler and a look.

**The trap in "something is drawn"**: a character Windows has no glyph for also produces ink — a little box. A test
that counts ink pixels in the emoji's cell passes on a transcript full of empty boxes. So there are two, and the
second one compares the emoji's cell with the cell of a **private-use code point** (`U+E123`), which nothing has a
glyph for. If those two pictures were the same, the transcript would be full of boxes and the first test would still
be green.

Whether Windows paints the faces **in colour** is not answerable from a test and is not claimed anywhere: it is
step 2 of `docs/testing/v1.4-emoji-checklist.md`.

## What was typed is what is sent

`WhatWasTypedWithEmojiIsSentAndShownAsItWasTyped` puts `cà phê 😀🎉` into the box the way the Windows picker (Win + `.`)
and a paste do, sends it, and asks for it back — same characters in what the service recorded, same characters in the
transcript, box empty afterwards as it should be. Two accents and two faces in one sentence, because the interesting
case is not "an emoji" but the ordinary message where a face sits in the middle of words.

`FiveHundredEmojiArriveWhole` does the same over real loopback sockets with the longest legal message there is: 500
characters, 1000 UTF-16 units, 2000 UTF-8 bytes. The three numbers have to be reconciled by every part of the path,
and this is the test that says they were.

## The habit

Two rules came out of this, and they are the same rule the empty picture viewer (v1.2.2) and the invisible thumbnails
taught:

1. **A feature nobody has looked at is not a feature.** "Nothing rejects it" is not evidence that anything displays
   it. Put pixels in a bitmap and compare them with something that is definitely wrong.
2. **Count the thing the person counts.** Bytes for the radio, characters for the sentence, and never the same number
   used for both.
