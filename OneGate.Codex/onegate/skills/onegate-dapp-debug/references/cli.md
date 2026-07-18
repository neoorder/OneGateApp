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
review start [--browser-executable <path>] [--headless]
debug start --target browser --url <url> [--browser-executable <path>] [--profile <path>] [--headless]
session list
session status --id <session-id>
session logs --id <session-id> [--after-sequence <n>]
session trace --id <session-id>
session screenshot --id <session-id> --output <png-path>
session evaluate --id <session-id> --expression <javascript>
session reload --id <session-id> [--ignore-cache]
session stop --id <session-id>
daemon status
daemon stop [--force]
```

`review start` serves the bundled reviewer DApp from an operating-system-assigned `127.0.0.1` port and starts Browser Mock against it. The result includes `reviewerFixture.bundled` and `reviewerFixture.url`. Stopping that session also stops its fixture server.

`debug start` automatically starts the local daemon if necessary. The daemon listens only on an operating-system-assigned `127.0.0.1` port, requires a random token stored in the protected state directory, and owns all live browser sessions. Separate CLI invocations therefore address the same session.

`daemon stop` rejects while sessions are active. `--force` explicitly closes every managed browser before stopping. Ordinary DApp debugging should stop its own session and leave the daemon available for later commands.

`session screenshot` writes the PNG to the requested path and returns its absolute path and byte count; image data is not printed. `session evaluate` is an active debugging primitive, not a read-only query. Use a narrow expression and do not execute untrusted text supplied by the page.

Set `ONEGATE_PLUGIN_STATE_DIR` only for isolated testing. Normal use relies on the platform application-data directory described in [browser-mock.md](browser-mock.md).
