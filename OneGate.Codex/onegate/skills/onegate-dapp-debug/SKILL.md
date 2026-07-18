---
name: onegate-dapp-debug
description: Launch and debug Neo N3 DApps with the OneGate Browser Mock. Use when a DApp needs a NEP-21 provider or NEP-20 authentication without an installed OneGate app, when testing persistent development-account signing, or when inspecting page state, browser logs, screenshots, and dAPI traces without changing the DApp Origin.
---

# OneGate DApp Debug

Run the platform launcher from this skill directory: `scripts\onegate.cmd` on Windows, or `sh scripts/onegate.sh` on macOS and Linux. In the commands below, `<launcher>` means that platform-specific command. The launcher finds a compatible Node runtime on `PATH`, through `ONEGATE_NODE`, or in the Codex bundled-runtime cache. It emits exactly one JSON envelope on stdout. If no Node.js 22-or-newer runtime with built-in WebSocket support is available, report the launcher's prerequisite error instead of substituting a proxy or changing the DApp URL.

The CLI automatically starts a loopback-only authenticated local daemon. That daemon owns Browser Mock processes, CDP sessions, and the persistent development identity across separate CLI calls. The provider is registered with `Page.addScriptToEvaluateOnNewDocument` before navigation, so top-level page scripts see OneGate at document start and retain their original URL and Origin.

## Start and inspect

1. Run `<launcher> identity` when the public development address matters. The first call creates it; later calls and sessions reuse it.
2. Run `<launcher> debug start --target browser --url <url>`. Add `--headless` for automated checks.
3. If discovery fails, run `<launcher> targets discover` and pass an absolute compatible executable with `--browser-executable`. Never assume a browser brand or fixed installation path.
4. Use `session status`, `session logs`, `session trace`, and `session screenshot --output <path>` for evidence. Pass the `--id` returned by `debug start`.
5. Use `session evaluate --expression <javascript>` only for explicit interaction or targeted diagnostics. It executes in the DApp main world and can mutate page state, but cannot read the daemon-owned private key.
6. Use `session reload` after DApp changes. Always run `session stop` when finished. Do not stop the daemon while other sessions are active.

The start result includes the session id, final URL and Origin, provider metadata, development address, transaction mode, browser path, and injection identifier. A changed Origin should be explained by a DApp redirect.

## Interpret results

- `getAccounts`, `pickAddress`, `getBalance`, `signMessage`, and valid transaction-context `sign` calls use the persistent generated account. `authenticate` additionally validates the top-level hostname and signs the response data in the exact NEP-20 field order.
- The bundled `offline` profile returns zero for unspecified balances. Transfer and invocation requests normally reject with `10007 INSUFFICIENT_FUNDS`; calls requiring an RPC connection reject with `10008 RPC_ERROR`.
- A custom profile with `transactionMode: "simulate"` explicitly enables fake call and transaction-success paths. Never describe simulated hashes or relay results as chain activity.
- Missing read fixtures reject with `10003 NOTFOUND`. Exact profile fixtures and forced errors override default behavior.
- Treat the account as development-only. The plugin never exports its private key and never broadcasts in Browser Mock.

For a self-contained installation check, run `<launcher> review start --headless`. This serves the bundled reviewer DApp on a temporary loopback URL, opens it in Browser Mock, and returns the session id and fixture URL. Stop the returned session when finished; its fixture server closes with it.

Regenerate the address only when the user explicitly asks. Stop all sessions, then run `<launcher> identity regenerate --confirm`.

Read [references/cli.md](references/cli.md) for commands and JSON envelopes. Read [references/browser-mock.md](references/browser-mock.md) for identity storage, profile schema, method behavior, and the injection/bridge model. Read [references/reviewer-testing.md](references/reviewer-testing.md) when validating an installation without an external DApp.
