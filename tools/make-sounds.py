"""Make the two F10 cues, and make them hearable.

    python tools/make-sounds.py

The cues Lantern ships are generated, not recorded, and they are in here rather than in somebody's scratch folder
because the suite measures them: `SoundNotifierTests.TheTwoSoundsAreLoudEnoughAndLongEnoughToHear` refuses a cue
shorter than 0.2 s or quieter than half of full scale, which is only a sensible test if the thing it measures has a
source a person can read and change.

The first pair were honest little files - 0.12 s and 0.34 s of a sine peaking at -11.7 dBFS - which passed every
test the suite had, since it counts cues rather than listening to them, and which nobody could pick out of a room
where four people are talking. These are the same two cues, longer, louder, and unmistakably two different sounds:
a rising two-note for a message, a rising three-note in a lower register for a file that has arrived.

Format is what SoundPlayer can actually play: uncompressed PCM, 16-bit, mono, 22050 Hz. Anything else (ADPCM,
float, extensible) throws - and the notifier says so now, but it is a poor sort of feature that only makes a noise
on the third try.

Kept to the standard library so it runs anywhere Python does.
"""

import math
import os
import struct
import wave

RATE = 22050
PEAK = 0.75
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Lantern", "Media")


def note(frequency, seconds):
    """One struck-bell-ish note: the fundamental plus two quiet partials, a four-millisecond attack, exponential decay."""
    count = int(RATE * seconds)
    samples = []
    attack = int(RATE * 0.004)

    for n in range(count):
        t = n / RATE
        tone = (math.sin(2 * math.pi * frequency * t)
                + 0.45 * math.sin(2 * math.pi * frequency * 2 * t)
                + 0.18 * math.sin(2 * math.pi * frequency * 3 * t))

        envelope = min(1.0, n / attack) if attack else 1.0
        envelope *= math.exp(-3.0 * t / seconds)
        samples.append(tone * envelope)

    return samples


def write(name, notes):
    samples = []
    for frequency, seconds in notes:
        samples.extend(note(frequency, seconds))

    # Fade the tail out over the last ten milliseconds, so the file ends at silence rather than mid-swing: a
    # waveform that stops somewhere other than zero is a click.
    tail = min(len(samples), int(RATE * 0.010))
    for index in range(tail):
        samples[len(samples) - tail + index] *= 1.0 - index / tail

    loudest = max(abs(value) for value in samples) or 1.0
    scale = PEAK / loudest

    frames = b"".join(
        struct.pack("<h", max(-32768, min(32767, int(round(value * scale * 32767))))) for value in samples)

    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(frames)

    print("%-16s %6.3f s  %5d bytes  peak %.2f (%.1f dBFS)"
          % (name, len(samples) / RATE, os.path.getsize(path), PEAK, 20 * math.log10(PEAK)))


write("message.wav", [(988.0, 0.11), (1318.5, 0.24)])                   # B5 -> E6: something arrived
write("file-ready.wav", [(523.3, 0.13), (659.3, 0.13), (784.0, 0.34)])  # C5 E5 G5: the file is here
