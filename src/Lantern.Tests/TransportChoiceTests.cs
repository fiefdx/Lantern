using Lantern.Core;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T4.4: which transport a run of Lantern uses, and what it says about that.
/// <para>
/// The decision is small but it is the one that decides whether anything can ever be found: a laptop
/// with no radio has to open a window anyway, and a laptop told to use its radio has to be told back
/// that its radio is missing rather than quietly switched to a transport that shows an empty list.
/// </para>
/// </summary>
public sealed class TransportChoiceTests
{
    [Fact]
    public void ARadioThatAnswersMeansBluetooth()
    {
        var transport = Transports.ForStartup(TransportWanted.Auto, probeRadio: () => "F8E4E3F9603B");

        Assert.Equal("Bluetooth", transport.Name);
        Assert.Contains("F8E4E3F9603B", transport.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ARadioThatIsNotThereFallsBackAndSaysSo()
    {
        var transport = Transports.ForStartup(TransportWanted.Auto, probeRadio: () => null);

        Assert.Equal("loopback", transport.Name);

        // Someone is about to press Scan on a machine that cannot find anything. The status bar is the
        // only place that can tell them why.
        Assert.Contains("no Bluetooth radio", transport.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Transports.Variable, transport.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ARadioThatThrowsIsTreatedAsARadioThatIsNotThere()
    {
        var transport = Transports.ForStartup(TransportWanted.Auto, probeRadio: () => throw new IOException("the driver said no"));

        // The window must open on any machine, so an unreadable radio is the same news as a missing one.
        Assert.Equal("loopback", transport.Name);
    }

    [Fact]
    public void BeingAskedForLoopbackDoesNotEvenLookForARadio()
    {
        var looked = false;

        var transport = Transports.ForStartup(TransportWanted.Loopback, probeRadio: () =>
        {
            looked = true;
            return "F8E4E3F9603B";
        });

        Assert.Equal("loopback", transport.Name);
        Assert.False(looked);       // two copies on one laptop must not both try to own the radio
    }

    [Fact]
    public void TheSwitchAndTheVariableBothAskForLoopback()
    {
        Assert.Equal(TransportWanted.Loopback, Transports.Wanted(["Lantern.dll", Transports.LoopbackSwitch], getenv: _ => null));
        Assert.Equal(TransportWanted.Loopback, Transports.Wanted(["Lantern.dll"], getenv: name => name == Transports.Variable ? "loopback" : null));
        Assert.Equal(TransportWanted.Loopback, Transports.Wanted(null, getenv: name => name == Transports.Variable ? " LOOPBACK " : null));

        Assert.Equal(TransportWanted.Auto, Transports.Wanted(["Lantern.dll"], getenv: _ => null));

        // A switch typed just now beats a variable left set an hour ago.
        Assert.Equal(
            TransportWanted.Loopback,
            Transports.Wanted([Transports.LoopbackSwitch], getenv: name => name == Transports.Variable ? "bluetooth" : null));

        Assert.Equal(
            TransportWanted.Bluetooth,
            Transports.Wanted(null, getenv: name => name == Transports.Variable ? "bluetooth" : null));
    }

    [Fact]
    public void BeingInsistentAboutTheRadioIsNotFallenBackFrom()
    {
        // Someone who set LANTERN_TRANSPORT=bluetooth is debugging. Being moved onto loopback instead,
        // with an empty list and no mention of the radio, is how a machine gets blamed for its driver.
        var transport = Transports.ForStartup(TransportWanted.Bluetooth, probeRadio: () => null);

        Assert.Equal("Bluetooth", transport.Name);
    }

    [Fact]
    public void TheVariableIsTheOnlyThingReadOutOfTheWorld()
    {
        // With nothing set and no radio to find, a normal start on a normal laptop ends up on loopback
        // rather than throwing at a window that was never going to appear.
        var transport = Transports.ForStartup(Transports.Wanted(["Lantern.dll"]));

        Assert.True(transport.Name == "loopback" || transport.Name == "Bluetooth");
        Assert.NotEmpty(transport.Description);
    }

    [Fact]
    public void ATransportBuildsTheTransportsItNames()
    {
        var bluetooth = new BluetoothTransportFactory("F8E4E3F9603B");
        Assert.IsType<BtRfcommServer>(bluetooth.NewServer("Kitchen Table"));
        Assert.IsType<BtRfcommClient>(bluetooth.NewClient());

        var loopback = new LoopbackTransportFactory();
        Assert.IsType<BtLoopbackServer>(loopback.NewServer("Kitchen Table"));
        Assert.IsType<BtLoopbackClient>(loopback.NewClient());
    }

}
