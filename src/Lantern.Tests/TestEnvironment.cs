using System.Runtime.CompilerServices;
using Lantern.Core;

namespace Lantern.Tests;

/// <summary>
/// One thing before any test in this assembly runs: the process is not allowed to make a noise.
/// <para>
/// Four hundred windows open and close across a run of this suite, several of them through the same code the
/// program uses - and every one of them is perfectly capable of playing a wav file at whoever is reading the
/// output. A test cannot hear a beep, so there is nothing to gain and only a noise to lose.
/// </para>
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void QuietThisProcess() => SoundNotifier.Muted = true;
}
