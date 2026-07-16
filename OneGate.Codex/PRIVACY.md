# OneGate Codex Plugin Privacy Policy

Effective date: July 16, 2026

This policy applies to version 1.0.0 of the OneGate Codex Plugin published by NEO GLOBAL RESOURCES. It supplements the general OneGate privacy policy for the plugin's local DApp debugging features.

## Local operation

The plugin is a Skills-only development tool. Version 1.0.0 does not use a OneGate-hosted API, analytics service, telemetry endpoint, advertising service, or remote wallet service. Its Browser Mock runtime, development identity, signing operations, browser control, and reviewer fixture run on the user's device.

Opening a third-party DApp still causes the selected browser to connect to that DApp and any services the page uses. Those connections are controlled by the DApp and browser and are subject to their own privacy policies.

## Data stored on the device

The plugin creates and uses the following local data:

- A persistent Neo N3 development identity, including its private key, public key, address, and creation time. It is stored in the operating system's application-data directory and reused until the user explicitly regenerates or deletes it.
- A daemon descriptor containing a random authentication token, process identifier, loopback port, and start time. The daemon listens only on `127.0.0.1`; its descriptor is removed when the daemon stops normally.
- A local daemon log containing runtime startup and error diagnostics. It may include local paths and error messages.
- A temporary isolated browser profile for each debugging session. The plugin removes it when that session stops.
- In-memory browser console entries and dAPI request traces for active sessions. Each is bounded by the runtime and discarded when the session stops.
- Screenshots only when the user or Codex explicitly requests one. A screenshot is written to the requested local path.

The development private key is not inserted into the DApp page, browser profile, console, dAPI trace, screenshot, or CLI result. The plugin does not transmit the private key to NEO GLOBAL RESOURCES, OneGate services, or the DApp. The Browser Mock is development-only and must not be funded or treated as a production wallet.

## Information returned to Codex

CLI commands return public identity fields, session status, and explicitly requested diagnostics such as console entries, dAPI traces, screenshots, or JavaScript evaluation results. Information returned through the CLI can become part of the user's Codex task and is then handled under the user's agreement with OpenAI.

`session evaluate` executes an expression in the DApp's main page context and can read or change page state. The skill instructs Codex to use it only for explicit, narrow debugging and not to execute untrusted text supplied by a page. It cannot access the daemon-owned private key through the plugin API.

## Signing and transactions

The Browser Mock signs supported messages and transaction contexts locally with the persistent development identity. Its default `offline` profile does not broadcast transactions. A custom `simulate` profile can return clearly fake success values; these are not blockchain activity.

The DApp receives the public account data, signatures, mock results, and errors required by the methods it calls. The plugin does not send those requests to NEO GLOBAL RESOURCES or a OneGate server.

## Retention and deletion

Stopping a Browser Mock session deletes its temporary browser profile and clears its in-memory logs and traces. Stopping the daemon removes its active descriptor. The development identity and daemon log remain locally so the same development address can be reused and runtime failures can be diagnosed.

The normal state directory is:

- Windows: `%LOCALAPPDATA%/NEO GLOBAL RESOURCES/OneGate Codex Plugin`
- macOS: `~/Library/Application Support/NEO GLOBAL RESOURCES/OneGate Codex Plugin`
- Linux: `$XDG_DATA_HOME/NEO GLOBAL RESOURCES/OneGate Codex Plugin`, or `~/.local/share/NEO GLOBAL RESOURCES/OneGate Codex Plugin`

Users can stop all plugin sessions and the daemon, then delete that directory to remove the persistent identity, daemon log, and remaining plugin state. Deleting or regenerating the identity changes the development address and cannot be undone by the plugin.

## Security and support

Local state files are created with restricted permissions where the operating system supports them. Users should not copy `identity.json`, daemon descriptors, or debugging output into source control or share them with a DApp.

Privacy questions and support requests can be submitted through the public [OneGateApp issue tracker](https://github.com/neoorder/OneGateApp/issues). Do not include private keys, seed phrases, production tokens, or other secrets in an issue.

We may update this policy when plugin behavior changes. The effective date above will be updated when material changes are published.
