# Development and contributing

This guide covers building OneGate, working with its platform integrations, and preparing contributions.
For an overview of the app, see the [README](README.md).

## Development setup

Install the .NET 10 SDK and the .NET MAUI workloads needed for your target. Follow the
[MAUI installation guide](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0)
for the matching Android, Windows, or Apple toolchain.

- **Android:** install the Android SDK and JDK required by the MAUI workload, then use a device or emulator.
- **Windows:** use Windows with the Windows SDK and MSIX development tools. The app is packaged as MSIX.
- **iOS and macOS:** use a Mac with a compatible Xcode version. Device and distribution builds need matching
  signing identities and provisioning profiles. Set the signing options for your own developer account;
  the checked-in project settings reference the project's distribution identities.

Open [OneGateApp.slnx](OneGateApp.slnx), restore NuGet packages, and select `OneGateApp` as the startup project.
Choose a target framework and a device or emulator in your MAUI development environment. From the repository root,
an Android build can also be run with:

```sh
dotnet build OneGateApp/OneGateApp.csproj -f net10.0-android
```

## Repository layout

| Path | Purpose |
|---|---|
| [OneGateApp](OneGateApp) | MAUI wallet, chat, platform integrations, and UI resources |
| [OneGate.DebugProtocol](OneGate.DebugProtocol) | Shared protocol for encrypted DApp remote debugging |
| [OneGate.DebugProtocol.Tests](OneGate.DebugProtocol.Tests) | Remote-debugging protocol tests |
| [OneGate.Tools](OneGate.Tools/README.md) | Payment and authentication QR/deep-link utilities |
| [OneGate.Codex](OneGate.Codex/README.md) | Codex plugin, browser mock, and remote debugger |

## DApp development

The [command-line tools](OneGate.Tools/README.md) generate payment and authentication requests.
The [Codex plugin](OneGate.Codex/README.md) supports a browser mock and paired real-app sessions.

Developer Tools provides opt-in encrypted remote debugging on the same LAN, with QR pairing and persistent
trust. Compatible debuggers can inspect logs and traces, evaluate JavaScript, capture the visible WebView,
and approve requests or supply method-specific results where OneGate policy permits. The Codex plugin
implements this interface; the pairing and session protocol is open to other compatible debuggers.

Remote debugging is off by default and accepts HTTPS DApp URLs only. For remotely started sessions, explicit
approval from the trusted debugger replaces in-app confirmation on every network. Disabling Developer Tools
disconnects debuggers and closes remote sessions while preserving the trust list.

## Chat profiles

The SDK retries unfinished profile publications while the chat session is running. Submitted edits cannot
be discarded: wait for confirmation, retry, or publish a replacement. The discovery switch shows the last
submitted choice and can be reversed while unconfirmed. Pending nickname, bio and discovery edits are
stored per account and restored when settings or the app reopens. Reopening the bio editor restores the
submitted bio; cancelling the draft keeps its pending publication and retry available. Input is validated
before replacing pending edits (nickname: 256 UTF-8 bytes; bio: 2048), so rejected text remains only a draft.

## Validation

Build each affected platform and validate UI/native behavior on its device or emulator. Compilation,
platform acceptance and actual delivery are separate results; builds do not establish device behavior
or deployment readiness.

For changes to the shared remote-debugging protocol, run the existing test project:

```sh
dotnet test OneGate.DebugProtocol.Tests/OneGate.DebugProtocol.Tests.csproj
```

Plugin validation and packaging instructions are in [OneGate.Codex/README.md](OneGate.Codex/README.md).

## Contributing changes

Issues and pull requests are welcome.

Good contribution areas include:

- cross-platform compatibility improvements
- UI / UX polish
- localization updates
- wallet interoperability
- dApp integration improvements
- testing and CI/CD
- documentation improvements

For larger changes, opening an issue first is recommended.

In a pull request, describe the behavior changed, the platforms affected, and the checks performed.
State which platform behavior still needs device testing, and update the relevant documentation.
