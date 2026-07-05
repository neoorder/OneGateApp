# GameFi QA Checklist and Performance Gate

This checklist is for reviewing game dApps before they are listed or promoted
in OneGate. It is intentionally focused on game quality, mobile WebView
behavior, wallet integration, and evidence collection.

Screenshots and recordings used for review must be uploaded as GitHub issue or
PR assets. Do not commit QA artifacts to the repository.

## Required Review Evidence

Each game submission or game runtime PR should include:

- Android emulator or device name and OS version.
- iOS simulator or device name and OS version.
- OneGate app version or commit SHA.
- Game URL and origin.
- Screenshots for Android and iOS.
- Wallet integration notes, including which accepted OneGate dAPI methods are used.
- Known limitations and any exceeded performance budget.

## Submission Preflight

| Check | Required result |
| --- | --- |
| HTTPS entry URL | Game loads from HTTPS only. |
| Stable origin | Wallet/authentication domain matches the listed game origin. |
| Metadata | Name, icon, description, developer, language, and category are present. |
| Licensing | Brand, title, music, game art, and third-party IP usage are documented. |
| Screenshots | Uploaded externally, not committed to the repository. |

## Loading and Failure States

| Check | Required result |
| --- | --- |
| Initial paint | Loading UI appears before heavy assets download. |
| Progress | Large games show progress or staged loading. |
| Timeout | User sees retry or error state when CDN load stalls. |
| Offline | Game shows a clear offline or retry path. |
| WebGL unsupported | Game shows a recoverable unsupported-device state. |
| Resource failure | Missing image/audio/WASM does not leave a blank screen. |

## Mobile Layout

| Check | Required result |
| --- | --- |
| Portrait | Main menu and first playable screen fit without desktop overflow. |
| Landscape | Game is playable and safe areas are respected. |
| Rotation | Canvas/layout recovers after rotation. |
| One-handed touch | Primary controls are reachable and large enough. |
| Keyboard | Text inputs do not hide critical actions behind the software keyboard. |
| Safe areas | Notch, home indicator, and system bars do not cover gameplay controls. |

OneGate should not patch a third-party game's DOM or CSS to pass these checks.
The game owner should fix mobile layout issues in the game itself.

## Performance Gate

Use the following as review thresholds. These are not automatic runtime blocks.

| Metric | Ready | Needs fix | Escalate |
| --- | ---: | ---: | ---: |
| First visible loading UI | <= 2 s | 2-5 s | > 5 s or blank screen |
| First interaction on Wi-Fi simulator | <= 10 s | 10-20 s | > 20 s |
| Long-frame rate during first minute | < 8% | 8-15% | > 15% |
| Android-only PSS during first minute | < 300 MB | 300-450 MB | > 450 MB |
| First-load transfer size | <= 8 MB | 8-20 MB | > 20 MB |
| Largest single first-load asset | <= 5 MB | 5-10 MB | > 10 MB |

If a game exceeds a warning threshold, the reviewer should ask for asset
optimization, lazy loading, lower mobile resolution, or a documented reason.

## Wallet Integration

| Check | Required result |
| --- | --- |
| Provider detection | Game waits for OneGate provider or shows wallet-unavailable state. |
| User intent | Wallet prompts happen only after a user action. |
| `getAccounts` | Read-only account display works without triggering signing flows. |
| `authenticate` | Used only for website authentication, not generic permission grants. |
| `send` / `invoke` | Transaction prompts show meaningful in-game context before dAPI call. |
| Cancellation | User cancellation returns the game to a safe state. |
| Failure | RPC failure, rejected signature, and timeout are visible and recoverable. |
| Network | Game confirms it is on the expected Neo N3 network. |

Do not store private keys, seed phrases, wallet passwords, or full signing
payloads in game storage.

## Proposed or Unaccepted Capabilities

Do not require, document, or test proposed OneGate APIs as part of this
checklist until they are accepted and shipped. If a game needs an app-level
capability that is not part of the current dAPI surface, record it as a separate
product/API proposal instead of treating it as a QA requirement.

## Platform Test Matrix

| Scenario | Android | iOS |
| --- | --- | --- |
| Cold launch from Gaming tab | Required | Required |
| Reload current game | Required | Required |
| Background and resume | Required | Required |
| Rotate, if supported | Required | Required |
| Wallet connect/authentication | Required | Required |
| Transaction/signature cancellation | Required | Required |
| Offline or blocked network | Required | Required |
| Screenshot evidence | Required | Required |

## Reviewer Notes Template

Use this template in issue or PR comments:

```markdown
### Game QA

- Game:
- URL:
- OneGate build:
- Android device:
- iOS device:

### Results

- Loading:
- Layout:
- Performance:
- Wallet:
- Proposed or unaccepted API needs:
- Security/privacy:
- Known limitations:

### Evidence

- Android screenshot:
- iOS screenshot:
- Additional logs or diagnostics:
```

## Pass Criteria

A game is ready for listing or promotion when:

- Android and iOS both load without blank-screen failure.
- First interaction and performance metrics are within the gate or justified.
- Mobile layout is usable without OneGate-side DOM/CSS adaptation.
- Wallet prompts are user-initiated and recover from cancellation/failure.
- The game does not depend on proposed or unaccepted OneGate APIs.
- Review screenshots are uploaded externally and not committed to the repo.

## Non-goals

- No local transaction history database.
- No connected-app permission center.
- No persistent dApp trust bar.
- No OneGate-side per-game DOM or CSS patching.
- No committed screenshots, videos, or performance artifacts.
