using System.Media;
using Lantern.Domain;

namespace Lantern.Core;

/// <summary>The two sounds F10 asks for.</summary>
public enum SoundCue
{
    /// <summary>Something arrived in the room.</summary>
    Message,

    /// <summary>A file this window was waiting for is here, whole.</summary>
    FileReady,
}

/// <summary>
/// F10: the room makes a noise when something arrives in it, so that a chat window behind another window is
/// still worth having open.
/// <para>
/// Two sounds, not one: the difference between "somebody said something" and "the file has arrived" is the
/// difference between glancing over and coming back to the machine.
/// </para>
/// <para>
/// The sounds are two small wav files next to the program rather than the system's scheme, because the system
/// scheme is different on every machine and can be set to silence by whoever sat down first - and a feature
/// that depends on somebody's sound settings is a feature that does not work in the lab. Replacing the files
/// changes the sound without changing the code, which is the reason they are files.
/// </para>
/// </summary>
public sealed class SoundNotifier : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<SoundCue, SoundPlayer?> players = [];
    private readonly Action<SoundCue> play;

    /// <summary>Whether to keep a list of what was played - only when somebody injected a player, which is how a test counts cues.</summary>
    private readonly bool records;

    public SoundNotifier()
        : this(null)
    {
    }

    /// <summary>
    /// Plays each cue some other way - which is how a test counts the cues without a speaker, and how a
    /// machine with no audio keeps its chat.
    /// </summary>
    public SoundNotifier(Action<SoundCue>? player)
    {
        records = player is not null;

        // The default player is the machine's sound card, and a process with no sound card - a test run, a
        // service session - should not be trying to use one. An injected player is not a sound card, and is
        // left alone: it is how a test counts the cues it was given.
        play = player ?? (cue =>
        {
            if (Muted)
            {
                return;
            }

            var sound = PlayerFor(cue);
            if (sound is null)
            {
                // Said out loud once, because a file that did not come with the program is otherwise
                // indistinguishable from a machine with the volume down, and only one of those is fixable.
                NoticedSilence($"{NameOf(cue)} is not next to Lantern");
                return;
            }

            sound.Play();
        });
    }

    /// <summary>
    /// No speakers in this process. A test runner sets this once: four hundred windows opening and closing is
    /// four hundred chances to make a noise in somebody's headphones, and not one test can hear it. It says
    /// nothing about whether a transfer finished - only about whether anybody can hear that it did.
    /// </summary>
    public static bool Muted { get; set; }

    /// <summary>Off for this window - the mute a person would ask for, if the menu had one yet.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Why this window has not been able to make a noise, or null if nothing has gone wrong. The first reason
    /// only: a notifier that complained about every message would be worse than one that says nothing.
    /// </summary>
    /// <remarks>
    /// There because "the sound does not work" is otherwise undiagnosable from the outside: the cue is a fifth
    /// of a second, it may be swallowed by an open window on somebody else's machine, and every failure here is
    /// deliberately silent, because a chat message must never fail because of a sound card. That is right for
    /// the message and useless for the person, so the reason is kept and the window is told once - and the bar
    /// can say <i>no sound: message.wav is not next to Lantern</i> instead of nothing at all happening.
    /// </remarks>
    public string? WhySilent { get; private set; }

    /// <summary>The reason in <see cref="WhySilent"/>, on the way to whoever will say it out loud.</summary>
    public event Action<string>? CouldNotPlay;

    /// <summary>How many of each cue this notifier has been asked for, oldest first. Only filled in when a player was injected.</summary>
    private readonly List<SoundCue> played = [];

    /// <summary>What was played, for the tests and nobody else.</summary>
    public IReadOnlyList<SoundCue> Played => played;

    /// <summary>A message landed in some room this window is showing.</summary>
    public void MessageArrived(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Kind == MessageKind.FILE)
        {
            // Announcing a file is not the same as having it, and this notifier is asked again when the last
            // of the bytes turn up. Two noises for one delivery is one too many.
            return;
        }

        Sound(SoundCue.Message);
    }

    /// <summary>
    /// A transfer this window was watching is over - in either direction, which is why the window says so
    /// rather than the transfer: the transfer does not know whether it was this window's file or somebody
    /// else's being relayed past.
    /// </summary>
    public void FileDone(FileTransferJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Cancel.IsCancellationRequested)
        {
            // Stopped is not finished, and a cheerful noise after a failed transfer is the sort of thing that
            // makes people stop using the noise.
            return;
        }

        Sound(SoundCue.FileReady);
    }

    private void Sound(SoundCue cue)
    {
        if (!Enabled)
        {
            // This window was told to keep quiet, which is a decision about the noise and so about the
            // record of it too.
            return;
        }

        if (records)
        {
            // Recorded as well as played, so that a test can say "that was the file sound rather than the
            // message sound" without having to listen for it.
            lock (gate)
            {
                played.Add(cue);
            }
        }

        try
        {
            play(cue);
        }
        catch (Exception ex)
        {
            // No sound card, a desktop session with no window station, a wav truncated by a bad copy. None of
            // them is a reason a chat message fails to arrive, and none of them is worth interrupting a person
            // for - so the chat goes on and the window is told once, quietly, in the bar it already uses.
            NoticedSilence($"{NameOf(cue)} could not be played ({ex.Message})");
        }
    }

    /// <summary>Keeps the first reason and says it once. The window decides whether to show it; the chat never waits for an answer.</summary>
    private void NoticedSilence(string reason)
    {
        lock (gate)
        {
            if (WhySilent is not null)
            {
                return;
            }

            WhySilent = reason;
        }

        try
        {
            CouldNotPlay?.Invoke(reason);
        }
        catch (Exception)
        {
            // A window that cannot be told about the silence is not a second reason to stop chatting.
        }
    }

    /// <summary>The name a person can look for in the folder next to Lantern.exe.</summary>
    private static string NameOf(SoundCue cue) => Path.GetFileName(PathFor(cue));

    /// <summary>
    /// Where the two wav files live: next to the program, so replacing a file changes the sound.
    /// <para>
    /// Settable for the same reason the sounds are files rather than resources - somebody may want them somewhere
    /// of their own - and because "the sound is not next to Lantern" is a thing a window has to be able to say,
    /// which is not testable without somewhere that has no sounds in it. Tests that move this back and forth run
    /// one at a time, like the rest of the suite (see AssemblyInfo).
    /// </para>
    /// </summary>
    public static string SoundDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Media");

    /// <summary>Where a cue's sound lives: next to the program, so replacing the file changes the sound.</summary>
    public static string PathFor(SoundCue cue)
        => Path.Combine(SoundDirectory, cue == SoundCue.Message ? "message.wav" : "file-ready.wav");

    private SoundPlayer? PlayerFor(SoundCue cue)
    {
        lock (gate)
        {
            if (players.TryGetValue(cue, out var known))
            {
                return known;
            }

            // Said by the caller, not from in here: this runs holding the gate, and whoever is told about a
            // missing sound is a window that may be busy with something else.
            var path = PathFor(cue);
            var player = File.Exists(path) ? new SoundPlayer(path) : null;

            // A missing file is remembered as missing: without that, every message in a room re-opens the
            // question of whether this machine has any sounds.
            players[cue] = player;

            return player;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var player in players.Values)
            {
                player?.Dispose();
            }

            players.Clear();
        }
    }
}
