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

## User files and chat avatars

Avatars belong to the Meshline account and are shared across its devices; contact remarks change only the
local display name. Chat settings offers photo selection, drag/zoom crop, preview and removal. The app
corrects orientation, fills transparency white and removes source metadata, producing a static 512 × 512
JPEG up to 128 KiB. The selected basename is kept with a `.jpg` extension.

The app uploads before publishing `AccountProfile.Avatar`. Upload failure leaves the profile unchanged.
Each accepted crop starts a new operation; retry keeps its `uploadId` for up to 24 hours and refreshes the
request timestamp and signature. After that window the user must select and upload again. Successful
upload followed by failed or unconfirmed publication preserves the reference per account, including after
reopening settings. Nickname, bio and discovery edits do not discard that pending avatar.

The SDK retries unfinished profile publications while the chat session is running. Submitted edits cannot
be discarded: wait for confirmation, retry, or publish a replacement. The discovery switch shows the last
submitted choice and can be reversed while unconfirmed. Pending nickname, bio and discovery edits are
stored per account and restored when settings or the app reopens. Reopening the bio editor restores the
submitted bio; cancelling the draft keeps its pending publication and retry available. Input is validated
before replacing pending edits (nickname: 256 UTF-8 bytes; bio: 2048), so rejected text remains only a draft.
Cancelling selection or cropping keeps the previous pending publication; a new crop replaces it only after upload and submission to the
SDK. Leaving settings or switching accounts cancels the current call, not a saved publication. Removing
uses `FieldUpdate<ContentReference>.Delete` and leaves other profile fields unchanged. The app reconciles
its retry state when the SDK confirms the corresponding avatar.

Each intentional upload creates a new record and URL, even for identical content; retries return the same
record. The server retains the latest three avatar uploads per account, independently of publication.
Older records enter a seven-day grace period and are deleted by a later maintenance run. Re-uploading old
content does not restore its old URL or extend its deadline. A published avatar can therefore expire after
three later uploads, and an SDK retry can still publish that expired URL. Select and upload another image
to replace it. Profile removal does not delete stored versions or previously downloaded copies.
The [server file guide](https://github.com/neoorder/OneGateServer/blob/master/docs/files.md) defines upload
signing, idempotency, limits, storage and deployment; deploy that service and its migration before clients.
Uploads do not depend on notification settings or a push declaration.

All chat surfaces use the same fixed-size avatar presentation. Until a verified image is available, show
an initial from the contact remark or nickname, otherwise the default person icon; addresses never supply
initials. Visible profiles refresh on page entry, foreground return, manual refresh and every five minutes.
A confirmed removal restores the fallback immediately; loading must not block messages or move their layout.

Downloads accept only unencrypted static JPEG, PNG or WebP up to 1 MiB and 4096 × 4096 pixels. Public HTTPS
is required for the original URL and redirects, without cookies or account credentials. Size, SHA-256 and
decoding are verified before display; valid cached content is reused by hash, including offline. Asynchronous
results remain tied to the current account and view so navigation and list reuse cannot display another avatar.

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
