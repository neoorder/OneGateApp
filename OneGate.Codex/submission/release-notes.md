# Release notes

## Unreleased

Real-app DApp debugging workflow.

- Adds a remote-debug interface that is not tied to a specific debugger, with this plugin providing the first compatible remote debugger through encrypted QR pairing and persistent mutual trust on the local network.
- Adds Bonjour/mDNS discovery, real DApp windows, visible-WebView screenshots, console capture, dAPI traces, reload, stop, and JavaScript evaluation.
- Adds explicit approve/reject commands for any real-wallet dAPI request OneGate exposes as pending, keeps other calls visible in the trace, and fails closed on disconnect or timeout.
- Allows an approval to carry an optional JSON result that OneGate validates according to the target RPC method.
- Uses trusted remote-debugger approval instead of ordinary in-app confirmation for remotely started DApp sessions on every network.
- Retains Browser Mock as the default target.

## OneGate 1.0.2

DApp catalog submission workflow.

- Adds a guided Skill for DApp, game, tool, and other OneGate catalog listing requests.
- Collects reusable public project metadata and validates it against the canonical GitHub issue form.
- Checks required URLs, exact catalog choices, likely duplicate requests, and accidental secret exposure before drafting.
- Shows the exact public issue and requires the form confirmations before any GitHub write.
- Supports authenticated GitHub connector or CLI submission, with a form-link and completed-draft fallback.

## OneGate 1.0.1

Protocol-correctness update for NEP-20 authentication.

- Corrects the signed response-data order to `nonce`, `timestamp`, `network`, `hash`, `action`, and `domain`.
- Rejects malformed uint64 nonces and challenge timestamps outside the five-minute clock tolerance.
- Compares the challenge domain with the DApp hostname case-insensitively.

## OneGate 1.0.0

Initial Skills-only release for the official Plugins Directory.

- Adds a document-start NEP-21 Browser Mock that preserves the DApp URL and Origin.
- Adds one persistent local Neo N3 development identity for account access and real P-256 signing.
- Adds an authenticated loopback daemon for browser sessions, logs, screenshots, evaluation, and dAPI traces.
- Adds offline and explicit simulation profiles without blockchain broadcasting.
- Adds Windows and macOS/Linux launchers that discover compatible Node runtimes without requiring `node` on PATH.
- Adds a bundled reviewer DApp and one-command end-to-end review flow.
