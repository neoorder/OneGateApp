# OneGate Codex Plugin Privacy Policy

Effective date: July 18, 2026

This policy applies to version 1.0.2 of the OneGate Codex Plugin published by NEO GLOBAL RESOURCES. It supplements the general OneGate privacy policy for the plugin's local DApp debugging and public listing-request features.

The installed OneGate app exposes a remote-debug interface that is not tied to a specific debugger. This policy covers only the remote debugger implementation bundled with the OneGate Codex Plugin; another compatible remote debugger is governed by its own privacy terms.

## Local operation

The plugin is a Skills-only development tool. Its Browser Mock runtime, development identity, signing operations, browser control, reviewer fixture, debug-target gateway, and diagnostic state run on the user's computer. Version 1.0.2 does not use a OneGate-hosted submission API, analytics service, telemetry endpoint, advertising service, cloud relay, or remote wallet service.

When the user pairs an installed OneGate app through this plugin, the plugin's remote debugger daemon and the OneGate debug target communicate directly over the same local network. Pairing is initiated by a short-lived QR code and the user’s confirmation in OneGate Developer Tools. Later connections use Bonjour/mDNS discovery only to locate a previously trusted remote debugger. The encrypted debug protocol authenticates both peers and does not send traffic through NEO GLOBAL RESOURCES or OneGate servers.

Opening a third-party DApp still causes the selected browser to connect to that DApp and any services the page uses. Those connections are controlled by the DApp and browser and are subject to their own privacy policies.

## DApp listing submissions

The listing skill prepares a request for the public `neoorder/OneGateApp` GitHub issue tracker. It does not publish automatically. Before an issue is created, the skill instructs Codex to show the exact title and body and obtain the user's authorization and the three confirmations required by the public DApp submission form.

If the user authorizes publication, the selected authenticated GitHub connector, GitHub CLI, or browser sends the issue to GitHub. The project name, descriptions, URLs, developer or publisher, public contact information, networks, wallet methods, mobile notes, security notes, and other supplied listing details then become public and are handled and retained by GitHub and the repository maintainers. The plugin does not receive or store the user's GitHub credentials.

The listing workflow is for public information only. It instructs Codex not to submit private keys, seed phrases, credentials, tokens, unpublished API keys, private endpoints, or other secrets.

## Data stored on the device

The plugin creates and uses the following local data:

- A persistent Neo N3 development identity, including its private key, public key, address, and creation time. It is stored in the operating system's application-data directory and reused until the user explicitly regenerates or deletes it.
- A separate persistent P-256 remote-debugger identity and trusted-debug-target records for real-OneGate pairing. A trusted-debug-target record contains the debug-target id, public identity, display name, platform, encrypted-channel reconnect secret, and connection timestamps. It does not contain the OneGate wallet private key, seed phrase, password, or biometric data.
- A daemon descriptor containing a random authentication token, process identifier, loopback port, and start time. The daemon listens only on `127.0.0.1`; its descriptor is removed when the daemon stops normally.
- A local daemon log containing runtime startup and error diagnostics. It may include local paths and error messages.
- A temporary isolated browser profile for each debugging session. The plugin removes it when that session stops.
- In-memory browser console entries and dAPI request traces for active sessions. Each is bounded by the runtime and discarded when the session stops.
- Screenshots only when the user or Codex explicitly requests one. A screenshot is written to the requested local path.

The installed OneGate app separately stores its debug-target identity, trusted-remote-debugger public identity, and reconnect secret in operating-system secure storage. Real-app console entries, dAPI traces, pending approvals, and deferred-operation state are bounded in memory. Turning Developer Tools off closes remote-debugger connections and remote DApp sessions but does not silently delete the trust list; the user can remove remote debuggers from Developer Tools.

The development private key is not inserted into the DApp page, browser profile, console, dAPI trace, screenshot, or CLI result. The plugin does not transmit the private key to NEO GLOBAL RESOURCES, OneGate services, or the DApp. The Browser Mock is development-only and must not be funded or treated as a production wallet.

## Information returned to Codex

CLI commands return public identity fields, paired debug-target names and ids, session status, pending dAPI method names and parameters, and explicitly requested diagnostics such as console entries, dAPI traces, screenshots, or JavaScript evaluation results. Information returned through the CLI can become part of the user's Codex task and is then handled under the user's agreement with OpenAI.

Public project information gathered for a listing, the resulting draft, and GitHub submission results can also become part of the user's Codex task.

`session evaluate` executes an expression in the DApp's main page context and can read or change page state. The skill instructs Codex to use it only for explicit, narrow debugging and not to execute untrusted text supplied by a page. It cannot access the daemon-owned private key through the plugin API.

## Signing and transactions

The Browser Mock signs supported messages and transaction contexts locally with the persistent development identity. Its default `offline` profile does not broadcast transactions. A custom `simulate` profile can return clearly fake success values; these are not blockchain activity.

A paired real OneGate session uses the wallet and network selected in the installed OneGate app. Every dAPI request is included in the session trace. OneGate determines which requests require remote approval, and any dAPI method can appear as pending; the plugin does not classify methods. Pending requests wait for explicit remote-debugger approval or rejection and fail closed after a timeout or disconnect. An approval can include an arbitrary JSON result selected by the user; it is transmitted to OneGate, included in the in-memory trace, and returned in the CLI result, so it should not contain secrets. OneGate validates and consumes it only according to the specific RPC method. In a remotely started DApp session, remote approval replaces the ordinary in-app confirmation on every network. Starting such a session delegates approval authority to the trusted remote debugger and accepts the associated wallet risk.

The DApp receives the public account data, signatures, mock results, and errors required by the methods it calls. The plugin does not send those requests to NEO GLOBAL RESOURCES or a OneGate server.

## Retention and deletion

Stopping a Browser Mock session deletes its temporary browser profile and clears its in-memory logs and traces. Stopping a real-app session clears its in-memory logs, traces, approvals, and deferred operations; it does not remove mutual trust. Stopping the daemon removes its active descriptor. The development identity, remote-debugger identity, trusted-debug-target records, and daemon log remain locally so identities and trust can be reused and runtime failures can be diagnosed.

Deleting local plugin state does not remove a submitted GitHub issue. The submitter must use GitHub to edit or close the issue, subject to GitHub's permissions and retention rules.

The normal state directory is:

- Windows: `%LOCALAPPDATA%/NEO GLOBAL RESOURCES/OneGate Codex Plugin`
- macOS: `~/Library/Application Support/NEO GLOBAL RESOURCES/OneGate Codex Plugin`
- Linux: `$XDG_DATA_HOME/NEO GLOBAL RESOURCES/OneGate Codex Plugin`, or `~/.local/share/NEO GLOBAL RESOURCES/OneGate Codex Plugin`

Users can run `debug-target forget --id <debug-target-id> --confirm` to remove one trusted OneGate debug target from the remote debugger. They can stop all plugin sessions and the daemon, then delete the state directory to remove both persistent identities, all remote-debugger-side debug-target trust, the daemon log, and remaining plugin state. Deleting or regenerating the development identity changes its Neo address and cannot be undone by the plugin. Remote-debugger-side deletion does not edit the OneGate app’s trust list; that remote debugger can be removed independently in OneGate Developer Tools.

## Security and support

Local state files are created with restricted permissions where the operating system supports them. Users should not copy `identity.json`, `remote-debugger-identity.json`, `remote-debug-targets.json`, daemon descriptors, pairing QR images, or debugging output into source control or share them with a DApp. Pairing QR images contain a short-lived secret and should be deleted or treated as expired after pairing.

Privacy questions and support requests can be submitted through the public [OneGateApp issue tracker](https://github.com/neoorder/OneGateApp/issues). Do not include private keys, seed phrases, production tokens, or other secrets in an issue.

We may update this policy when plugin behavior changes. The effective date above will be updated when material changes are published.
