---
name: onegate-dapp-debug
description: Launch and debug Neo N3 DApps with either the local OneGate Browser Mock or a paired real OneGate app. Use for NEP-21/NEP-20 testing, real-wallet development sessions, persistent development-account signing, and inspecting page state, logs, screenshots, approval requests, and dAPI traces without changing the DApp Origin.
---

# OneGate DApp Debug

Run the platform launcher from this skill directory: `scripts\onegate.cmd` on Windows, or `sh scripts/onegate.sh` on macOS and Linux. In the commands below, `<launcher>` means that platform-specific command. The launcher finds a compatible Node runtime on `PATH`, through `ONEGATE_NODE`, or in the Codex bundled-runtime cache. It emits exactly one JSON envelope on stdout. If no Node.js 22-or-newer runtime with built-in WebSocket support is available, report the launcher's prerequisite error instead of substituting a proxy or changing the DApp URL.

The CLI automatically starts a loopback-only authenticated local daemon. That daemon owns Browser Mock processes, paired debug-target connections, sessions, and persistent local identities across separate CLI calls. For real-app sessions, it is the remote debugger implementation bundled with this plugin. OneGate's app-side remote-debug interface is not tied to Codex or any specific debugger and can also be implemented by other trusted tools. Browser Mock remains the default target and registers the provider before navigation. The optional `onegate` target controls a real OneGate DApp window over an end-to-end encrypted same-LAN connection.

## Start and inspect

1. Use target `browser` unless the user explicitly asks for a real installed OneGate app, real wallet state, or platform WebView behavior.
2. For Browser Mock, run `<launcher> identity` when the public development address matters, then `<launcher> debug start --target browser --url <url>`. Add `--headless` for automated checks.
3. For a real app, first run `<launcher> debug-target list`. If no trusted debug target is online, run `<launcher> debug-target pair start --output <png>`, have the user enable OneGate Developer Tools and scan that QR code, then poll `debug-target pair status --id <pairing-id>`. Start with `<launcher> debug start --target onegate --debug-target <debug-target-id> --url <https-url>`; omit `--debug-target` only when exactly one debug target is connected.
4. If browser discovery fails, run `<launcher> targets discover` and pass an absolute compatible executable with `--browser-executable`. Never assume a browser brand or fixed installation path.
5. Use `session status`, `session logs`, `session trace`, and `session screenshot --output <path>` for evidence. Pass the `--id` returned by `debug start`.
6. In a real-app session, poll `session requests` and approve or reject each pending dAPI request explicitly with `session approve` or `session reject`; never script blanket approval. OneGate decides which calls require approval, and any method can appear as pending, so do not infer approval policy from the method name. If the user explicitly chooses a return value, pass that exact JSON with `session approve --result <json>`; do not invent a result or interpret it in the plugin. A disconnect or approval timeout rejects the pending request. Other calls continue and remain visible in `session trace`.
7. Use `session evaluate --expression <javascript>` only for explicit interaction or targeted diagnostics. Add `--defer` for a long-running expression and poll `session operation`. Evaluation executes in the DApp main world and can mutate page state.
8. Use `session reload` after DApp changes. Always run `session stop` when finished. Do not stop the daemon while other sessions are active.

Browser Mock start includes the session id, final URL and Origin, provider metadata, development address, transaction mode, browser path, and injection identifier. Real-app start includes the session id, debug-target id, HTTPS URL and Origin, and startup state. A changed Origin should be explained by a DApp redirect.

## Interpret results

- `getAccounts`, `pickAddress`, `getBalance`, `signMessage`, and valid transaction-context `sign` calls use the persistent generated account. `authenticate` additionally validates the top-level hostname and signs the response data in the exact NEP-20 field order.
- The bundled `offline` profile returns zero for unspecified balances. Transfer and invocation requests normally reject with `10007 INSUFFICIENT_FUNDS`; calls requiring an RPC connection reject with `10008 RPC_ERROR`.
- A custom profile with `transactionMode: "simulate"` explicitly enables fake call and transaction-success paths. Never describe simulated hashes or relay results as chain activity.
- Missing read fixtures reject with `10003 NOTFOUND`. Exact profile fixtures and forced errors override default behavior.
- Treat the account as development-only. The plugin never exports its private key and never broadcasts in Browser Mock.
- A real OneGate app uses its actual selected wallet and network. In a remotely started DApp session, explicit approval delegates authority to the trusted remote debugger and replaces ordinary in-app confirmation on every network. Treat pairing and remote sessions as high-trust operations.
- Pairing creates persistent mutual trust. Session stop does not erase it. Use `debug-target forget --id <debug-target-id> --confirm` and the OneGate Developer Tools trust list when trust must be removed.

For a self-contained installation check, run `<launcher> review start --headless`. This serves the bundled reviewer DApp on a temporary loopback URL, opens it in Browser Mock, and returns the session id and fixture URL. Stop the returned session when finished; its fixture server closes with it.

Regenerate the address only when the user explicitly asks. Stop all sessions, then run `<launcher> identity regenerate --confirm`.

Read [references/cli.md](references/cli.md) for commands and JSON envelopes. Read [references/browser-mock.md](references/browser-mock.md) for identity storage, profile schema, method behavior, and the injection/bridge model. Read [references/onegate-app.md](references/onegate-app.md) for pairing, trust, approval, network, and real-app security behavior. Read [references/reviewer-testing.md](references/reviewer-testing.md) when validating an installation without an external DApp.
