# Pairing two laptops for Lantern — how-to (and what each failure means)

Written for the M4 field test (T4.7), but useful any time two machines refuse to talk. Nothing here is
specific to Lantern except the service GUID, and the checks are run with `tools/BtSpike`, which is in the
solution precisely so that a failure in the field can say **"transport"** or **"Lantern"** instead of
"something".

Two Windows 10/11 laptops. Call them **A** (the one that will host) and **B** (the one that will join).

```
dotnet build Lantern.sln
dotnet run --project tools/BtSpike -- <command>
```

---

## 1. Both radios on, both machines awake and plugged in

Settings → Bluetooth & devices → Bluetooth **On**. Airplane mode off on both. Laptops that fall asleep
mid-test produce a timeout that looks exactly like a broken program.

On **A**, ask the stack what it thinks it is:

```
dotnet run --project tools/BtSpike -- radio
```

```
InTheHand.Net.Bluetooth.BluetoothRadio.Default:
  LocalAddress = F8E4E3F9603B        ← write this down, B needs it
  Name         = DESKTOP-G0ARUQV
  Mode         = Connectable         ← not "Passive", not "RadioOff"
```

If this line fails, stop: the problem is the machine, not Lantern. Nothing further will work.

## 2. Pair, from B, once

Pairing is between *machines*, not between programs, and it survives restarts — do it once per pair of
laptops, in advance, not in front of an audience.

**Route 1 (Settings, the one that shows you what is happening).** On B: Settings → Bluetooth & devices →
**Add device** → Bluetooth → wait for A's name → click it → a passkey appears on both screens → type it
on the other one and accept. Windows asks to install a driver for the peer; let it.

**Route 2 (command line), when Settings spins and finds nothing.** The other machine must be
*discoverable* for this to see it — Windows keeps a machine discoverable only for a few minutes after you
open its Bluetooth page, which is the single most common reason a scan comes back empty.

```
B:  dotnet run --project tools/BtSpike -- pair 1234 F8E4E3F9603B
```

`pair - <address>` lets Windows choose (it will show a passkey to be typed/accepted). `pairRequest`
returns false — printing `Windows refused, or the user on the other machine said no` — when the peer is
not discoverable, the passkey was mistyped, or somebody pressed Cancel.

Then, on B:

```
dotnet run --project tools/BtSpike -- paired
  F8E4E3F9603B  "DESKTOP-G0ARUQV"  class=LaptopComputer
```

B now knows A exists. **Pairing one way is enough for B to connect to A**, which is all Lantern needs:
only the joining machine opens a connection.

## 3. Prove the transport before blaming the program

On **A**, publish the Lantern service without starting the chat:

```
dotnet run --project tools/BtSpike -- host 60
  listening for c1a5ee70-6c7a-4a54-9b7a-4c2b6f9d1a01 as "Lantern" (active: True)
```

`active: True` means Windows accepted the service registration. If it does not say that, no amount of
Lantern fixing will help — see the table.

On **B**, while A is still hosting:

```
dotnet run --project tools/BtSpike -- raw F8E4E3F9603B
  connect to F8E4E3F9603B service c1a5ee70-…-4c2b6f9d1a01 (SDP lookup then connect):
      ok
```

`ok` means the SDP lookup found the service and RFCOMM carried a connection. Then run the real handshake
instead of only the connect:

```
dotnet run --project tools/BtSpike -- probe F8E4E3F9603B
  connected to DESKTOP-G0ARUQV.
  frame HELLO_ACK server="…" nickname="BtSpike host"
```

Those two lines are the M4 exit criterion in miniature: RFCOMM carries a frame both ways. A and B may
then be closed and Lantern started for real.

## 4. What each failure means

`raw` prints a socket code. These are the ones measured with the spike, in the words the stack used.

| what you saw | what it means | what to do |
| --- | --- | --- |
| `10060` — "connection attempted for a while without a correct reply" (≈5 s) | B could not get through to A: A is not hosting, asleep, out of range, or its Bluetooth is off | on A run `host 60` and watch it; check `radio` says `Connectable`; bring the machines closer; is A's Lantern still running (it holds the service, so only one host per machine) |
| `10049` — "the requested address is not valid in its context" (immediate) | the address is not one this stack will dial — a typo, or the machine's own address | re-read the address from A's `radio` output; note `F8E4E3F9603B`, `F8-E4-E3-F9-60-3B` and `f8e4e3f9603b` are the same machine |
| `10022` — "invalid argument" (immediate) | the async connect overload, which the spike found unreliable in this library version — see `docs/notes/T4.1-bluetooth-spike.md` | use `raw`/`probe` for diagnosis; Lantern's own connect does not take that path |
| `host` says it cannot listen | another program holds the service — often a Lantern window from the previous test, still listening | close every Lantern on that machine, then retry. One host per machine |
| `pair` returns false / `Windows refused` | peer not discoverable, passkey mistyped, or Cancel pressed | open the peer's Bluetooth page (that is what makes it discoverable), retry; if it was paired before and behaves oddly: Settings → Bluetooth → the peer → **Remove device**, then pair again |
| `scan` finds nothing | normal. Windows machines are discoverable only briefly; phones hide themselves by default | do not trust the scan: pair in advance, or type the address. See D2 in the spike note |
| everything above succeeded but Lantern cannot join | now it really is Lantern | `probe` still working while Lantern fails means the fault is in Lantern, and it is worth a defect report with the address and the time |

## 5. Before the field test proper

- [ ] Both machines: `radio` prints an address and `Mode = Connectable`
- [ ] B: `paired` lists A
- [ ] A: `host 60` reports `active: True`
- [ ] B: `raw <A>` says `ok`, then `probe <A>` prints a `HELLO_ACK`
- [ ] Third machine (if available): repeat from it as well — T4.5 is about three clients at once
- [ ] Record on `docs/testing/m4-field-test.md` which steps needed a second attempt, and how often
