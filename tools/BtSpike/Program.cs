using System.Reflection;
using System.Text;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Lantern.Core;
using Lantern.Transport;

namespace Lantern.Tools.BtSpike;

/// <summary>
/// T4.1 — the Bluetooth spike. A probe, not a product: it asks this machine's Bluetooth stack the
/// questions the M4 design depends on, and prints the answers in the words the stack used, so that
/// <c>docs/notes/T4.1-bluetooth-spike.md</c> can quote evidence instead of a forum post.
/// </summary>
/// <remarks>
/// It deliberately shares <see cref="LanternProtocol.ServiceId"/> and <see cref="FrameCodec"/> with
/// the application. A spike that invents its own GUID or its own framing proves that the radio works,
/// which was never the doubtful part.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
            Console.WriteLine("(cancelling - Ctrl+C again to stop waiting for the stack)");
        };

        try
        {
            return command switch
            {
                "api" => ApiReport.Write(),
                "radio" => RadioReport.Write(),
                "paired" => Paired(),
                "scan" => await ScanAsync(Seconds(args, 1, 12), cancel.Token),
                "host" => await HostAsync(Seconds(args, 1, 0), cancel.Token),
                "probe" => await ProbeAsync(args, cancel.Token),
                "raw" => Raw(args),
                "pair" => Pair(args),
                _ => Help(),
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            BtSpike - what this machine's Bluetooth stack will do for Lantern (task T4.1)

              api                 what 32feet.NET actually offers this build (service lookup? pairing?)
              radio             the local adapter(s): address, name, and whether the radio is on
              paired            the devices Windows already remembers, which is what joining can rely on
              scan [seconds]    inquire for that long (default 12) and list what answers
              host [seconds]    listen as a Lantern host (0 = until Ctrl+C), print what arrives
              probe <address> [seconds]   connect to the Lantern service on that machine, handshake once
              pair <pin> <address>        ask Windows to pair with that machine
              raw <address> [guid]        where a connection actually fails: socket family, SDP, or connect

            Exit codes: 0 fine, 1 it failed, 2 the stack does not support it, 130 cancelled.
            """);
        return 0;
    }

    // ------------------------------------------------------------------- commands

    /// <summary>
    /// The devices Windows remembers. This matters more than it sounds: on Windows an *outgoing*
    /// RFCOMM connection to a device that has never been paired prompts the user on both ends, so
    /// "join a chat" cannot be one click unless the machines were paired first.
    /// </summary>
    private static int Paired()
    {
        var remembered = Support(client => client.PairedDevices.ToArray(), "list paired devices");
        Console.WriteLine($"paired devices: {remembered.Length}");
        foreach (var device in remembered)
        {
            Describe(device);
        }

        return 0;
    }

    private static async Task<int> ScanAsync(int seconds, CancellationToken cancel)
    {
        var timeout = TimeSpan.FromSeconds(seconds);
        Console.WriteLine($"inquiting for {seconds}s ...");

        // The inquiry is a blocking call in a library that offers no async version of it, so it goes
        // on a worker thread and the cancellation is a lie we tell politely: the stack stops when it
        // feels like it.
        var found = await Task.Run(() => Support(client =>
        {
            client.InquiryLength = timeout;
            return client.DiscoverDevices();
        }, "inquire for nearby devices"), cancel).ConfigureAwait(false);

        Console.WriteLine($"answered: {found.Count}");
        foreach (var device in found)
        {
            Describe(device);
        }

        return 0;
    }

    /// <summary>
    /// Registers the Lantern service with the local SDP database and waits for one connection. This is
    /// the step that can only be proven on real hardware: whether Windows lets an ordinary application
    /// publish a private service GUID at all, and what it says if it does not.
    /// </summary>
    private static async Task<int> HostAsync(int seconds, CancellationToken cancel)
    {
        using var listener = new BluetoothListener(LanternProtocol.ServiceId)
        {
            ServiceName = "Lantern",
        };

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not listen for service {LanternProtocol.ServiceId}: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"listening for {LanternProtocol.ServiceId} as \"Lantern\" (active: {listener.Active})");
        Console.WriteLine("this machine's address: " + string.Join(", ", RadioReport.LocalAddresses()));

        var deadline = seconds <= 0 ? DateTime.MaxValue : DateTime.Now.AddSeconds(seconds);

        while (!cancel.IsCancellationRequested && DateTime.Now < deadline)
        {
            if (!listener.Pending())
            {
                await Task.Delay(100, cancel).ConfigureAwait(false);
                continue;
            }

            var client = listener.AcceptBluetoothClient();
            _ = HandleAsync(client, cancel);
        }

        listener.Stop();
        Console.WriteLine("stopped listening.");
        return 0;
    }

    /// <summary>One accepted connection: read frames, answer the handshake, print what was seen.</summary>
    private static async Task HandleAsync(InTheHand.Net.Sockets.BluetoothClient client, CancellationToken cancel)
    {
        Console.WriteLine($"connection from {Name(client)}");

        using (client)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var stream = client.GetStream();
                var frame = await FrameCodec.ReadAsync(stream, timeout.Token).ConfigureAwait(false);

                if (frame is null)
                {
                    Console.WriteLine("  the other end closed before sending a frame.");
                    return;
                }

                Console.WriteLine($"  frame {frame.Type} nickname=\"{frame.Header.Nickname}\" body={frame.Body.Length} bytes");

                await FrameCodec.WriteAsync(stream, Frame.HelloAck("BtSpike host"), timeout.Token).ConfigureAwait(false);
                Console.WriteLine("  answered HELLO_ACK.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("  no frame within 10s - a connection with no HELLO is a scan, not a user.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  connection failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The client side of the field test: find the Lantern service on another machine, complete the
    /// handshake, and report what the handshake said. If this works between two laptops, the transport
    /// is done and everything above it is already tested over loopback.
    /// </summary>
    private static async Task<int> ProbeAsync(string[] args, CancellationToken cancel)
    {
        if (args.Length < 2 || !BluetoothAddress.TryParse(args[1], out var address))
        {
            Console.WriteLine("usage: probe <bluetooth address> [seconds]");
            return 1;
        }

        var timeout = TimeSpan.FromSeconds(args.Length > 2 ? ParseSeconds(args[2]) : 20);
        Console.WriteLine($"connecting to {address} service {LanternProtocol.ServiceId} ...");

        var client = new BluetoothClient();

        try
        {
            await client.ConnectAsync(address, LanternProtocol.ServiceId).WaitAsync(timeout, cancel).ConfigureAwait(false);
            Console.WriteLine($"connected to {Name(client)}.");

            var stream = client.GetStream();
            await FrameCodec.WriteAsync(stream, Frame.Hello("BtSpike"), cancel).ConfigureAwait(false);

            var frame = await FrameCodec.ReadAsync(stream).WaitAsync(timeout, cancel).ConfigureAwait(false);

            Console.WriteLine(frame is null
                ? "the host closed the connection without answering."
                : $"frame {frame.Type} server=\"{frame.Header.ServerName}\" nickname=\"{frame.Header.Nickname}\"");
            return frame is null ? 1 : 0;
        }
        finally
        {
            client.Close();
            client.Dispose();
        }
    }

    /// <summary>
    /// The connect that <c>probe</c> performs is three things at once: is RFCOMM available to a
    /// .NET socket at all, can the stack find the service on the far machine (an SDP lookup), and can
    /// it connect. When a connect fails the one error code does not say which, and the difference
    /// decides whether Lantern has a bug, a missing pairing, or the wrong library - so each is tried on
    /// its own, in the order the stack does them.
    /// </summary>
    private static int Raw(string[] args)
    {
        if (args.Length < 2 || !BluetoothAddress.TryParse(args[1], out var address))
        {
            Console.WriteLine("usage: raw <bluetooth address> [service guid]");
            return 1;
        }

        var service = args.Length > 2 ? Guid.Parse(args[2]) : LanternProtocol.ServiceId;

        // There is no System.Net.Sockets.AddressFamily.Bluetooth in .NET 10 - 32feet talks to the
        // stack through its own socket type - so the layers cannot be probed with BCL sockets here.
        // What can be probed is the connect itself, and the same connect aimed at a service the far
        // machine is known to publish, which separates "our SDP lookup is wrong" from "this stack will
        // not carry an outgoing RFCOMM connection at all".
        Say($"connect to {address} service {service} (SDP lookup then connect)", () =>
        {
            using var client = new BluetoothClient();
            client.Connect(new BluetoothEndPoint(address, service, -1));
            Console.WriteLine($"      connected to {client.RemoteMachineName}; stream can be written: {client.GetStream().CanWrite}");
            client.Close();
        });

        return 0;
    }

    /// <summary>One attempt, one line, whatever it thinks.</summary>
    private static void Say(string what, Action attempt)
    {
        Console.WriteLine();
        Console.WriteLine(what + ":");

        try
        {
            attempt();
            Console.WriteLine("      ok");
        }
        catch (Exception ex)
        {
            var code = ex is System.Net.Sockets.SocketException socket ? $" (socket code {(int)socket.SocketErrorCode})" : string.Empty;
            Console.WriteLine($"      {ex.GetType().Name}{code}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pairing from the command line, because the pairing story is the first thing that goes wrong on
    /// two laptops and the second thing to try has to be possible without hunting through Settings.
    /// </summary>
    private static int Pair(string[] args)
    {
        if (args.Length < 3 || !BluetoothAddress.TryParse(args[2], out var address))
        {
            Console.WriteLine("usage: pair <pin or - > <bluetooth address>");
            return 1;
        }

        var pin = args[1] == "-" ? null : args[1];
        Console.WriteLine($"pairing with {address} {(pin is null ? "(let Windows choose)" : $"with PIN {pin}")} ...");

        var paired = BluetoothSecurity.PairRequest(address, pin, null);
        Console.WriteLine(paired ? "paired." : "Windows refused, or the user on the other machine said no.");
        return paired ? 0 : 1;
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Run something that needs a radio, and say plainly in the stack's own words if it cannot.</summary>
    private static T Support<T>(Func<BluetoothClient, T> action, string what)
    {
        try
        {
            using var client = new BluetoothClient();
            return action(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"cannot {what}: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine("This is the answer to one of the spike's questions, not a bug: record it.");
            Environment.Exit(2);
            throw; // unreachable; keeps the compiler off the "not all paths" path
        }
    }

    private static void Describe(BluetoothDeviceInfo device)
    {
        Console.WriteLine($"  {device.DeviceAddress}  \"{Name(device)}\"  class={Class(device)}");
    }

    /// <summary>A name is the one thing the stack sometimes refuses to give for a device it can see.</summary>
    private static string Name(BluetoothDeviceInfo device)
    {
        try
        {
            return device.DeviceName;
        }
        catch (Exception ex)
        {
            return $"<no name: {ex.GetType().Name}>";
        }
    }

    private static string Name(InTheHand.Net.Sockets.BluetoothClient client)
    {
        try
        {
            return client.RemoteMachineName;
        }
        catch (Exception ex)
        {
            return $"<{client.Client.RemoteEndPoint}> ({ex.GetType().Name})";
        }
    }

    private static string Class(BluetoothDeviceInfo device)
    {
        try
        {
            return device.ClassOfDevice.Device.ToString();
        }
        catch (Exception ex)
        {
            return $"<unreadable: {ex.GetType().Name}>";
        }
    }

    private static int Seconds(string[] args, int index, int fallback)
        => args.Length > index ? ParseSeconds(args[index]) : fallback;

    private static int ParseSeconds(string text)
        => int.TryParse(text, out var value) && value >= 0 ? value : throw new FormatException($"\"{text}\" is not a number of seconds");

    private static int Fail(Exception ex)
    {
        Console.WriteLine($"FAILED {ex.GetType().Name}: {ex.Message}");

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            Console.WriteLine($"  caused by {inner.GetType().Name}: {inner.Message}");
        }

        Console.WriteLine();
        Console.WriteLine(ex.StackTrace);
        return 1;
    }
}

/// <summary>
/// What the Bluetooth library we adopted actually exposes to this build. Written as reflection output
/// rather than as prose because the answer changed between 32feet versions, and because whether a
/// "look up which machines host this service" API exists decides the whole design of discovery (T4.3).
/// </summary>
internal static class ApiReport
{
    private static readonly string[] Interesting =
    [
        "InTheHand.Net.Sockets.BluetoothServiceCatalog",
        "InTheHand.Net.Sockets.BluetoothAdapter",
        "InTheHand.Net.Bluetooth.BluetoothAdapter",
        "InTheHand.Net.Bluetooth.BluetoothRadio",
        "InTheHand.Net.Sockets.BluetoothListener",
        "InTheHand.Net.Sockets.BluetoothClient",
        "InTheHand.Net.Bluetooth.BluetoothSecurity",
    ];

    public static int Write()
    {
        var assembly = typeof(BluetoothClient).Assembly;

        Console.WriteLine($"assembly: {assembly.GetName().Name} {assembly.GetName().Version}");
        Console.WriteLine();

        Console.WriteLine("does this version offer a way to look up \"which machines host service X\"?");
        foreach (var candidate in new[]
                 {
                     "InTheHand.Net.Sockets.BluetoothServiceCatalog",
                     "InTheHand.Net.Sockets.BluetoothService",
                     "InTheHand.Net.Bluetooth.ServiceDiscovery",
                 })
        {
            Console.WriteLine($"  {candidate}: {(assembly.GetType(candidate) is null ? "absent" : "PRESENT")}");
        }

        Console.WriteLine();
        Console.WriteLine("the three types Lantern needs:");

        foreach (var name in Interesting)
        {
            var type = assembly.GetType(name);
            Console.WriteLine($"  {name}: {(type is null ? "absent" : "present")}");

            if (type is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName)
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                Console.WriteLine($"      {Returns(method)} {method.Name}({Parameters(method)})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("every exported type in the library:");
        foreach (var type in Exported(assembly))
        {
            Console.WriteLine($"  {type.FullName}");
        }

        return 0;
    }

    private static IEnumerable<Type> Exported(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal);
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!).OrderBy(t => t.FullName, StringComparer.Ordinal);
        }
    }

    private static string Returns(MethodInfo method)
        => method.ReturnType.Name;

    private static string Parameters(MethodInfo method)
    {
        var parts = method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}");
        var builder = new StringBuilder();
        builder.AppendJoin(", ", parts);
        return builder.ToString();
    }
}

/// <summary>
/// The local adapter(s). Reflective because the type that answers this changed with the library
/// version, and because a spike that fails to compile on the day of the field test is worse than one
/// that prints "this build does not have BluetoothRadio".
/// </summary>
internal static class RadioReport
{
    private static readonly string[] Candidates =
    [
        "InTheHand.Net.Bluetooth.BluetoothRadio",
        "InTheHand.Net.Sockets.BluetoothAdapter",
        "InTheHand.Net.Bluetooth.BluetoothAdapter",
    ];

    public static int Write()
    {
        var adapters = Adapters();

        if (adapters.Count == 0)
        {
            // Rather than give up: print what the candidate types actually contain, because "how do we
            // tell the user their own address, which the other laptop has to type in" has to be
            // answered from this build of the library and not from documentation of an older one.
            Console.WriteLine("no local adapter found through the candidate types. What they do have:");
            DumpMembers();
            return 2;
        }

        foreach (var (label, adapter) in adapters)
        {
            Console.WriteLine($"{label}:");
            foreach (var property in adapter.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {property.Name} = {Value(adapter, property)}");
            }
        }

        return 0;
    }

    /// <summary>The address other machines have to type in, which is the only thing the field test needs.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        var addresses = new List<string>();

        foreach (var (_, adapter) in Adapters())
        {
            var property = adapter.GetType().GetProperty("LocalAddress");
            var value = Value(adapter, property);

            if (value is not null && value.ToString()!.Length > 0)
            {
                addresses.Add(value.ToString()!);
            }
        }

        return addresses;
    }

    private static void DumpMembers()
    {
        var assembly = typeof(BluetoothClient).Assembly;

        foreach (var name in Candidates)
        {
            var type = assembly.GetType(name);

            if (type is null)
            {
                continue;
            }

            Console.WriteLine("  " + name);

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Static
                        | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var obsolete = member.GetCustomAttribute<ObsoleteAttribute>() is null ? string.Empty : " [obsolete]";
                Console.WriteLine("      " + member.MemberType + " " + member.Name + obsolete);
            }
        }
    }

    private static List<(string Label, object Adapter)> Adapters()
    {
        var found = new List<(string, object)>();
        var assembly = typeof(BluetoothClient).Assembly;

        foreach (var name in Candidates)
        {
            var type = assembly.GetType(name);

            if (type is null)
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                // "BluetoothRadio.Default" is the one that answers here, and its name says nothing about
                // adapters - so match on what it returns as well as on what it is called.
                var yieldsThisType = property.PropertyType == type
                    || typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType);

                if (yieldsThisType
                    || property.Name.Contains("Radio", StringComparison.Ordinal)
                    || property.Name.Contains("Adapter", StringComparison.Ordinal))
                {
                    foreach (var item in Items(property))
                    {
                        found.Add(($"{name}.{property.Name}", item));
                    }
                }
            }
        }

        return found;
    }

    private static IEnumerable<object> Items(PropertyInfo property)
    {
        object? value;

        try
        {
            value = property.GetValue(null);
        }
        catch
        {
            return [];
        }

        return value switch
        {
            null => [],
            IEnumerable<object> items => items,
            _ => [value],
        };
    }

    private static string Value(object owner, PropertyInfo? property)
    {
        if (property is null)
        {
            return "<none>";
        }

        try
        {
            return property.GetValue(owner)?.ToString() ?? "<null>";
        }
        catch (Exception ex)
        {
            return $"<{ex.GetType().Name}>";
        }
    }

    private static string Value(object owner, string propertyName)
        => Value(owner, owner.GetType().GetProperty(propertyName));
}
