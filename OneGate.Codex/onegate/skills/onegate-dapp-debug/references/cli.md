# OneGate local CLI

Run commands from the skill directory with the platform launcher:

```text
Windows:       scripts\onegate.cmd <command>
macOS/Linux:  sh scripts/onegate.sh <command>
```

The launcher accepts `ONEGATE_NODE` as an explicit Node executable. Otherwise it checks `node` on `PATH`, then the local Codex bundled-runtime cache. Node.js 22 or newer with built-in WebSocket support is required.

Every invocation writes one compact JSON object to stdout. Success uses:

```json
{"ok":true,"command":"identity","result":{}}
```

Failure exits nonzero and uses:

```json
{"ok":false,"command":"debug start","error":{"code":"INVALID_ARGUMENT","message":"--url is required."}}
```

Do not scrape human text from the result. Parse `ok`, then consume `result` or `error.code` and `error.message`.

## Commands

```text
doctor
targets discover
identity
identity regenerate --confirm
debug-target pair start --output <png> [--name <debugger-name>]
debug-target pair status --id <pairing-id>
debug-target pair cancel --id <pairing-id>
debug-target list
debug-target forget --id <debug-target-id> --confirm
review start [--browser-executable <path>] [--headless]
debug start --target browser --url <url> [--browser-executable <path>] [--profile <path>] [--headless]
debug start --target onegate [--debug-target <debug-target-id>] --url <https-url>
session list
session status --id <session-id>
session logs --id <session-id> [--after-sequence <n>]
session trace --id <session-id>
session screenshot --id <session-id> --output <png-path>
session requests --id <session-id>
session approve --id <session-id> --request-id <request-id> [--result <json>]
session reject --id <session-id> --request-id <request-id> [--reason <text>]
session evaluate --id <session-id> --expression <javascript> [--defer]
session operation --id <session-id> --operation-id <operation-id>
session reload --id <session-id> [--ignore-cache]
session stop --id <session-id>
daemon status
daemon stop [--force]
```

`review start` serves the bundled reviewer DApp from an operating-system-assigned `127.0.0.1` port and starts Browser Mock against it. The result includes `reviewerFixture.bundled` and `reviewerFixture.url`. Stopping that session also stops its fixture server.

`debug start` automatically starts the local daemon if necessary. Its command API listens only on an operating-system-assigned `127.0.0.1` port and requires a random token stored in the protected state directory. A separate LAN TCP listener accepts only cryptographically authenticated paired OneGate debug targets; it is not an unauthenticated command API.

`debug-target pair start` writes a scannable PNG to the requested path and returns its pairing id, expiry, and output metadata without printing the QR secret. Pairing expires after two minutes. `debug-target list` reports persisted debug targets and current online state. Forgetting a debug target is intentionally confirmation-gated.

The `onegate` target requires OneGate Developer Tools to be enabled and accepts only HTTPS URLs. When more than one debug target is connected, `--debug-target` is required. Other dAPI calls continue into `session trace`; use `session requests` and an explicit approve/reject command for every pending request. The CLI does not classify methods because OneGate may require approval for any dAPI call. An approval may include an optional JSON result; the CLI validates its JSON syntax and transmits the value unchanged. Omit `--result` unless the user explicitly selected a return value. Omission is distinct from `--result null`.

`daemon stop` rejects while sessions are active. `--force` explicitly closes every managed browser before stopping. Ordinary DApp debugging should stop its own session and leave the daemon available for later commands.

`session screenshot` writes the PNG to the requested path and returns its absolute path and byte count; image data is not printed. On the real-app target it captures only the visible WebView viewport. `session evaluate` is an active debugging primitive, not a read-only query. Use a narrow expression and do not execute untrusted text supplied by the page. `--defer` returns an operation id instead of waiting for a long expression.

Set `ONEGATE_PLUGIN_STATE_DIR` only for isolated testing. Normal use relies on the platform application-data directory described in [browser-mock.md](browser-mock.md).
