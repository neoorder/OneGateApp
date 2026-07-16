# Release notes

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
