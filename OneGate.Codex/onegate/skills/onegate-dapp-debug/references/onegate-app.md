# OneGate debug target

OneGate acts as a debug target for a remote-debug interface that is not tied to a specific debugger. The plugin's `onegate` target uses one compatible remote debugger implementation; other trusted debugging tools can implement the same app-side pairing and session protocol in the future. It controls DApp windows in an installed OneGate app on Windows, Android, iOS, or Mac Catalyst. The interface is opt-in: the user must enable the existing Developer Tools switch in OneGate. Disabling that switch closes the remote-debugger connection and its remote sessions but retains trusted-remote-debugger records.

## Pairing and discovery

`debug-target pair start --output <png>` creates a two-minute QR invitation containing one local TCP endpoint, an ephemeral pairing secret, and the remote debugger's public identity. The invitation is not printed in the CLI JSON result. The user scans it from OneGate Developer Tools and confirms the displayed remote-debugger name and fingerprint.

Both peers authenticate independent persistent P-256 identities, derive session keys with signed ephemeral P-256 ECDH and HKDF-SHA256, and protect every subsequent frame with AES-256-GCM, monotonic sequence numbers, and replay rejection. A new random reconnect secret is exchanged inside the paired encrypted channel. This remote debugger implementation stores its identity and trusted debug targets only in local plugin state; OneGate keeps its side in operating-system secure storage.

After pairing, the daemon advertises `_onegate-debug._tcp.local` with Bonjour/mDNS. An enabled OneGate developer mode reconnects only when the advertised debugger id already exists in its trust store. QR TCP endpoints remain the fallback when multicast discovery is unavailable. Only one remote debugger can be active in an app instance.

## Sessions and approvals

Real-app start accepts only `https:` DApp URLs. Each start creates a separate DApp window where the platform supports app windows. Status, visible-WebView PNG screenshot, reload, stop, console capture, dAPI trace, and main-world JavaScript evaluation are available through the common `session` commands.

Every dAPI call is recorded in `session trace`. OneGate's RPC method decides whether a call becomes a pending request, and any dAPI method may require approval; the remote debugger does not classify methods. Read pending methods and parameters with `session requests`, then explicitly approve or reject each request id. An approval can carry an optional arbitrary JSON result. The remote debugger transmits that result without interpreting it, while the specific OneGate RPC method decides whether and how to validate and consume it. Other calls continue, and there is no approve-all operation. A remote-debugger disconnect or two-minute approval timeout fails the pending request closed. Logs and traces are bounded in memory and are discarded with the remote session; a screenshot is written only to the explicit CLI output path.

In a remotely started DApp session, an explicit remote-debugger approval and its optional result replace the ordinary in-app interaction on every network according to the target RPC method's policy. Starting such a session delegates approval authority to the trusted remote debugger and accepts the associated wallet risk.

Trust outlives sessions. `debug-target forget --id <debug-target-id> --confirm` removes the debug target from the remote debugger. The user can independently remove a remote debugger from OneGate Developer Tools. Forgetting either side requires a new QR pairing before another connection succeeds.
