# OneGate Codex Plugin

The installable plugin bundle is in [`onegate`](onegate). Version 1.0.0 is a Skills-only plugin for the official Plugins Directory. It implements the Browser Mock target; future OneGate desktop and mobile adapters can use the same CLI and local runtime.

The skill invokes a stable JSON CLI through `scripts/onegate.cmd` on Windows or `scripts/onegate.sh` on macOS/Linux. These launchers find Node on `PATH`, through `ONEGATE_NODE`, or in Codex's bundled-runtime cache, so Codex does not depend on a globally configured `node` command. The CLI automatically starts an authenticated loopback-only daemon that owns live browser sessions and the development identity across separate Codex calls. There is no public MCP endpoint or fixed remote service.

Browser Mock starts an isolated CDP-compatible Chromium-family browser, registers a private dAPI binding and `Page.addScriptToEvaluateOnNewDocument`, and only then navigates to the DApp. It does not proxy or rewrite the page, so ordinary navigation preserves the DApp URL and Origin. Browser discovery is portable and accepts an explicit executable; no browser brand is required.

On first use, the runtime creates one persistent Neo N3 development account in the operating system's application-data directory. The private key stays in the daemon. DApps receive normal NEP-21 account and signing results through the injected provider, and the same address is reused until `identity regenerate --confirm` is explicitly requested.

The bundled profile uses `offline` transaction mode: signing works with real P-256 signatures, balances default to zero, transactions fail with insufficient-funds or RPC errors, and nothing is broadcast. A custom profile can set `transactionMode` to `simulate` to opt into clearly fake transaction-success paths. `review start` also serves an included DApp fixture on a temporary loopback URL, giving reviewers a reproducible end-to-end test without external hosting.

## Validate

Node.js 22 or newer is required. Run the tests from the project directory:

```text
node --test tests/browser_mock.test.mjs
```

The integration test drives the public CLI through the daemon and uses any available compatible browser in headless mode. If none is installed, only the browser-dependent test is skipped. Plugin and Skill validators are also run before packaging `onegate` for submission.

## Package

After validation, build both release archives from the project directory:

```text
pwsh -File scripts/Package-OneGatePlugin.ps1
```

The script reads the release version from `onegate/.codex-plugin/plugin.json`, includes hidden plugin metadata, verifies required ZIP entries, and prints each archive's byte count and SHA-256 hash. It creates:

- `dist/onegate-dapp-debug-skill-<version>.zip` for the official Skills-only submission;
- `dist/onegate-plugin-<version>.zip` for complete local plugin distribution.

Existing release archives are protected by default. Pass `-Force` only when intentionally rebuilding the same version.
