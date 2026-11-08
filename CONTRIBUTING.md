# Development and contributing

This guide covers building OneGate, working with its platform integrations, and preparing contributions.
For an overview of the app, see the [README](README.md).

## Development setup

Install the .NET 10 SDK and the .NET MAUI workloads needed for your target. Follow the
[MAUI installation guide](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0)
for the matching Android, Windows, or Apple toolchain.

- **Android:** install the Android SDK and JDK required by the MAUI workload, then use a device or emulator.
  Firebase notification testing requires Google Play services.
- **Windows:** use Windows with the Windows SDK and MSIX development tools. The app is packaged as MSIX.
- For **iOS and macOS**, use a Mac with Xcode that matches the installed
  [.NET Apple workload](https://aka.ms/xcode-requirement). Use your own signing identities and matching
  provisioning profiles for device and distribution builds.

Open [OneGateApp.slnx](OneGateApp.slnx), restore NuGet packages, and select `OneGateApp` as the startup project.
Choose a target framework and a device or emulator in your MAUI development environment. From the repository root,
an Android build can also be run with:

```sh
dotnet build OneGateApp/OneGateApp.csproj -f net10.0-android
```

Platform notification setup is described below. Android builds require the Firebase client configuration file;
service-account keys, APNs signing keys, and WNS client secrets belong on the server.

## Repository layout

| Path | Purpose |
|---|---|
| [OneGateApp](OneGateApp) | MAUI wallet, chat, platform integrations, and UI resources |
| [OneGate.DebugProtocol](OneGate.DebugProtocol) | Shared protocol for encrypted DApp remote debugging |
| [OneGate.DebugProtocol.Tests](OneGate.DebugProtocol.Tests) | Remote-debugging protocol tests |
| [OneGate.Translation.Apple](OneGate.Translation.Apple) | Swift translation bridge and .NET binding |
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

## Package versions

Direct package references keep their versions in `OneGateApp/OneGateApp.csproj`.
`OneGateApp/Directory.Packages.props` contains only AndroidX alignment references for dependencies
that would otherwise be transitive. NuGet imports these references for Android builds only.
Central Package Management is not enabled, so the project retains its inline `Version` attributes.
When updating Firebase or Google Play bindings, restore the project and check the resolved dependency
graph, then validate Android Release builds and device behavior. When switching Android bindings,
use a clean Android intermediate directory to avoid stale resource IDs.

## Chat and notifications

For chat translation changes, keep processing on the device and keep translation requests, results, and
native error descriptions out of logs and persistent storage. Require approval before downloading language
models and respect the selected network policy. Preserve requested writing systems, including Simplified
and Traditional Chinese, and use the complete translation for copying and full-text reading.

Keep Google's [attribution assets](https://cloud.google.com/translate/attribution) unmodified and visible beside
Android translation results, including full-text reading. Credit Google Translate and link to Google Cloud
Translation in the Android app description and user help.

Settings > Notification settings has two switches, both enabled by default. The master switch applies
to the entire installation; the chat switch belongs to the current chat device and is independent for
other accounts or replacement devices. Changes are saved offline and reconciled when connectivity returns
or the app enters the foreground. The master switch takes precedence without changing the chat preference.

Android, iOS and Mac Catalyst request system permission after chat is enabled and the main window is visible.
A declined prompt is not repeated automatically; the settings page links to system settings and refreshes
permission on return. Ordinary preference changes and platform registration need no wallet unlock.
Remote push also requires the device certificate to declare the gateway: missing authorization prompts
for wallet approval and can be skipped until later. Finish setup or Retry in Notification settings resumes
incomplete publication without replacing the device. Turning notifications off unregisters the platform
binding without revoking this declaration.

The client registers at `https://onegate.space/api/push/devices`, using audience `https://onegate.space`
and its authorized device key. The certificate declares the callback `https://onegate.space/api/push/notifications`.
Foreground sessions, platform identifier changes and language changes refresh the binding. Failed requests
retry while the app is visible and after restart without overwriting newer choices. Temporary registration
failures retain a usable binding; denied permission unregisters it on the next refresh. Suspension retains
it, while wallet deletion attempts cleanup before ending chat; deletion remains possible offline.

Until the server processes an unregister request, Android/iOS may still display remote notifications.
Notifications handled by the running desktop app check both switches before display. Notifications contain
a sender/group name and generic text in the app's language, never message content. A tap opens a direct
conversation only when the current account and device match; group or unknown notifications open chat home.
Notification data neither creates messages nor marks them read.

Desktop chat remains connected while the process runs, including with an inactive or minimized window.
Verified live messages can produce local notifications; startup and reconnect catch-up stay silent.
Local and remote delivery share a recent-message deduplication history; an active main window suppresses banners.
Returning refreshes messages, unread state and registration. Android/iOS suspend their chat connection;
a disconnected or exited desktop client relies on remote APNs/WNS delivery.

Meshline's signed `notify` flag requests reminders for chat messages and contact consent, not device-state,
grant-update or account-sync events. Relay must support this flag and issue callbacks only for `true`;
an omitted flag does not request push. Server credentials, registration contracts and callback deployment
are documented in the [server push guide](https://github.com/neoorder/OneGateServer/blob/master/docs/push.md).

### Android / Firebase Cloud Messaging

1. Create or select a project in the [Firebase console](https://console.firebase.google.com/) and register
   Android package **org.neoorder.onegate**. Google Analytics is optional and disabled in the app manifest.
2. Download `google-services.json` to `OneGateApp/Platforms/Android/google-services.json`. This client
   configuration can be versioned; the MAUI project includes it with the `GoogleServicesJson` build action.
3. Build and run on a device or emulator with Google Play services, enable chat, and allow notifications.

Builds require this file. Android 13+ requests `POST_NOTIFICATIONS`; older supported versions check system
notification settings. The app creates the `onegate_messages` channel before registration and enables FCM
auto-initialization only after permission is granted. Missing Google Play services reports `ServiceUnavailable`.
The registered address is a Firebase installation ID (FID), including subsequent registration changes;
OneGateServer sends to `message.fid`. The notification small icon is `ic_stat_onegate`.

### iOS / Apple Push Notification service

1. Enable **Push Notifications** for `org.neoorder.onegate` in the Apple Developer account.
2. Regenerate the development and distribution provisioning profiles with that capability.
3. Build with matching signing credentials and profile, enable chat, and allow notifications.

Keep `Platforms/iOS/Entitlements.plist`; the Apple SDK supplies `aps-environment` from the selected profile.
Do not choose Sandbox/Production based on Debug/Release. The client reads the embedded provisioning profile:
an absent profile defaults to production, while an unreadable or invalid profile, missing entitlement or
unsupported value reports `NotConfigured`. Registration failure does not prevent wallet or chat startup.
Foreground sessions request a fresh APNs registration; late callbacks recheck permission before use.

### macOS / Mac Catalyst

Enable **Push Notifications** for the Mac app identifier and regenerate development and Mac App Store
profiles. The bundle identifier must match server `Push:ApplicationId` (`org.neoorder.onegate`); the APNs
key must authorize that topic and environment. Mac Catalyst uses the same APNs service as iOS.

Keep `Platforms/MacCatalyst/Entitlements.plist`. The Apple SDK supplies `com.apple.developer.aps-environment`
from the selected profile, which the client reads from `Contents/embedded.provisionprofile`. As on iOS,
an absent profile means production; an invalid existing profile leaves the environment unknown.
Build, sign and validate on a Mac with Xcode and matching profiles.

### Windows / Windows Push Notification Services

The MSIX app supports local notifications without WNS credentials or a gateway declaration. Both notification
switches and Windows system permission must allow them; Windows has no mobile-style permission prompt.
Remote push is `NotConfigured` in this checkout until the client identity and activation entries are added.

Follow the [server Windows setup](https://github.com/neoorder/OneGateServer/blob/master/docs/push.md#windows-凭据与包身份)
for Entra registration, PFN mapping and server credentials, and the
[Windows App SDK quickstart](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/push-notifications/push-quickstart)
for MSIX push activation. The client needs:

- `OneGateApp/Resources/Raw/wns.json` with `clientId` (Entra Application ID) and `servicePrincipalObjectId`
  (the service principal's Object ID, not the app registration's Object ID).
- A matching MSIX COM activation entry whose class ID is the client ID and whose executable arguments are
  `----WindowsAppRuntimePushServer:`. The JSON file alone does not enable remote push.

Rebuild and install the signed MSIX after configuration. Foreground sessions request a fresh channel;
raw WNS delivery can display a local notification without opening a wallet window or connecting to Relay.
Clicks use the same account/device checks as mobile. Client secrets belong only on the server.

### Local gateway development

Debug builds optionally read `push-development.json` from the app-private data directory. Its `endpoint`
and `audience` override the registration URL and signing audience. The audience must exactly match the
server's public HTTPS origin, with no path, query or fragment. The callback is derived by trimming its
trailing slash and appending `/api/push/notifications`; there is no separate callback setting.

Registration accepts HTTPS or loopback HTTP. Android USB testing can use `adb reverse`; a test build must
also permit loopback HTTP if used. Relay callbacks still require publicly routable HTTPS. Release builds
ignore this file; remove it after testing. Deploy the callback and database migrations before enabling
client declarations. Firebase and APNs credentials are unaffected by the override.

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

Build each affected platform and validate UI/native behavior on its device or emulator. For avatars, cover
selection, crop cancellation and drag/zoom, upload and publication retry, account switching, offline cache,
profile removal and list reuse. For notifications, cover permission, both switches, foreground/background,
registration changes and notification clicks. Compilation, platform acceptance and actual delivery are
separate results; builds do not establish device behavior or deployment readiness.

For translation changes, verify language selection and support, model download approval and cancellation,
model deletion and offline use, full-text reading and copying, and scroll position and read state. Test
Android Debug and Release on target devices and networks. Build the Apple app on a Mac and verify system
download prompts on real devices. Verify that translation controls appear only on supported systems.

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
