# Lantern

A local Bluetooth group chat for Windows — C# / Windows Forms / .NET 10.

One machine **hosts** a chat server, the other machines **scan** for it and join
as clients. The host organises the chat into named **rooms**; every client picks
one room at a time. No accounts, no Wi‑Fi, no internet.

## Requirements

* Windows 10 or 11
* .NET 10 SDK (or Visual Studio 2022+ with the ".NET desktop development" workload)
* A Bluetooth radio on each machine (only needed from milestone **M4** onwards)

## Build and run

```powershell
dotnet build Lantern.sln
dotnet test  Lantern.sln                                  # unit tests

dotnet run --project src/Lantern/Lantern.csproj           # the app
```

The solution layout:

```
src/Lantern/            WinForms app (.NET 10)
  UI/                   forms, dialogs, status bar — talks only to IChatService
  Core/                 ChatServer, ChatClient, ClientSession, ServerBrowser,
                        SoundNotifier, IChatService
  Domain/               Message hierarchy, Room, User, FileTransferJob, validation
  Storage/              RoomDto, IRoomStore, JsonRoomStore (rooms.json)
  Transport/            IBtServer / IBtClient / IClientLink, FrameCodec,
                        loopback (TCP) transport and the real RFCOMM transport
src/Lantern.Tests/      xUnit unit + integration tests
docs/                   proposal, task breakdown, UML, wireframes, module notes
```

## Modes

| Mode | What it does |
|:--|:--|
| **Live** | Real `ChatServer` / `ChatClient` over the selected transport |
| **Loopback** | Same engine over localhost TCP — used for demos and tests when Bluetooth is unavailable |
| **Fake** | `FakeChatService`, in-memory dummy rooms/messages, no sockets at all — for UI work |

The transport is chosen at start-up; the UI is identical in all three because it
only ever sees `IChatService`.

## Roadmap

| Milestone | Theme |
|:--|:--|
| M0 | Project skeleton & app shell |
| M1 | Domain model & validation |
| M2 | Chat engine + wire protocol over loopback |
| M3 | Room persistence & room management |
| M4 | Real Bluetooth transport |
| M5 | Pictures, file transfer, sounds |
| M6 | Hardening, testing, release (`v1.0`) |

See [docs/TASK_BREAKDOWN.md](docs/TASK_BREAKDOWN.md) for the full task list and
[docs/PROJECT_PROPOSAL.md](docs/PROJECT_PROPOSAL.md) for the specification.
Working rules, branch policy and module owners are in [CONTRIBUTING.md](CONTRIBUTING.md).
