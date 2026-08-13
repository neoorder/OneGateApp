# Browser Mock reference

## Injection and private-key boundary

The plugin launches an isolated CDP-compatible Chromium-family process with a temporary user-data directory and loopback-only DevTools endpoint. It attaches to a blank page and performs this sequence:

1. Enable the Page, Runtime, and Log domains.
2. Register `Runtime.addBinding` for the private dAPI request bridge.
3. Register public provider configuration and `assets/nep21-mock.js` with `Page.addScriptToEvaluateOnNewDocument`.
4. Navigate to the exact DApp URL.
5. Confirm that the provider exists and the document is interactive or complete.

The provider is injected only into the top-level main world. No reverse proxy, HTML rewriting, isolated world, extension, or service worker is involved. Normal navigation therefore retains the target URL and Origin. A DApp redirect is reflected in the final status.

The page sends method names and arguments through the CDP binding. The local daemon validates each request, performs signing, and returns only public results to the originating execution context. The generated private key is never inserted into the browser profile, page configuration, DOM, trace, console, or `evaluate` result.

This implementation follows the [NEP-21 dAPI provider specification](https://github.com/neo-project/proposals/blob/master/nep-21.mediawiki) and mirrors current OneGate fields and method shapes where Browser Mock has the necessary local information. Its `authenticate` method implements the NEP-20 authentication scheme.

## NEP-20 authentication

`authenticate(challenge)` accepts the NEP-20 challenge payload and returns its response payload. The Browser Mock:

- requires `action: "Authentication"`, `grant_type: "Signature"`, support for `ECDSA-P256`, and the Neo N3 network magic;
- compares `challenge.domain` with the top-level page hostname, case-insensitively;
- accepts a uint64 nonce as a decimal string or a JavaScript safe integer and preserves its JSON representation in the response;
- rejects challenge timestamps more than five minutes behind or ahead of local time; and
- signs the binary fields in the exact NEP-20 order: `nonce`, response `timestamp`, `network`, account script `hash`, `action`, then `domain`.

The uint64 and uint32 fields are little-endian, `hash` is the 20-byte script hash, and the two strings use Neo `var_str` encoding. The 64-byte P-256 signature is returned as base64.

## Browser selection

`debug start` accepts `--browser-executable`, an absolute path to a browser implementing the required Chromium DevTools Protocol methods. If omitted, discovery checks configured environment variables, compatible commands on `PATH`, and common platform locations. These candidates are conveniences, not a browser-brand dependency.

## Persistent development identity

The first identity or session request creates `identity.json` under:

- Windows: `%LOCALAPPDATA%/NEO GLOBAL RESOURCES/OneGate Codex Plugin`
- macOS: `~/Library/Application Support/NEO GLOBAL RESOURCES/OneGate Codex Plugin`
- Linux: `$XDG_DATA_HOME/NEO GLOBAL RESOURCES/OneGate Codex Plugin`, or `~/.local/share/...`

`ONEGATE_PLUGIN_STATE_DIR` overrides the directory for isolated testing. The file contains the development private key and must not be copied into a DApp repository or profile. The runtime restricts file permissions where the OS supports it and exposes only address, script hash, public key, account contract, creation time, network, and state path through the identity command.

An invalid or corrupt identity file causes a visible error; the plugin does not silently replace the address. Explicit regeneration requires `confirm: true` and no active Browser Mock sessions.

## Profile format

Profiles contain public behavior only. Accounts are not configured in a profile because the persistent identity supplies the account.
The Browser Mock always supplies `provider.version` from the plugin manifest, so profiles do not declare it.

```json
{
  "id": "local-simulation",
  "transactionMode": "simulate",
  "provider": {
    "name": "OneGate Codex Plugin",
    "dapiVersion": "1.0",
    "network": 860833102,
    "supportedNetworks": [860833102]
  },
  "balances": [
    {
      "asset": "0x0123456789abcdef0123456789abcdef01234567",
      "account": "GENERATED_ACCOUNT_HASH_OR_ADDRESS",
      "value": "100000000"
    }
  ],
  "blockCount": 123456,
  "fixtures": [
    {
      "method": "getTokenInfo",
      "args": ["0x0123456789abcdef0123456789abcdef01234567"],
      "result": { "symbol": "TEST", "decimals": 8, "totalSupply": "1000000000" }
    }
  ],
  "errors": [
    {
      "method": "invoke",
      "code": 10006,
      "message": "Canceled by fixture"
    }
  ]
}
```

Fixture and error entries match `method` plus an exact JSON-serialized `args` array. Omit `args` to match every call to that method. An error entry takes precedence over a fixture. Never store private keys, seed phrases, production tokens, or other secrets in a profile.

## Method behavior

| Method | `offline` default | `simulate` default |
| --- | --- | --- |
| `getAccounts`, `pickAddress` | Persistent generated account | Same |
| `getBalance` | Profile match or `"0"` | Same |
| `authenticate`, `signMessage` | Real P-256 signature | Same |
| `sign` | Real signature on a valid Neo transaction context | Same |
| `send`, `invoke` | `10007 INSUFFICIENT_FUNDS` | Fake transaction hash signed into the simulation result |
| `makeTransaction`, `relay` | `10008 RPC_ERROR` | Fake local context or relay result; no broadcast |
| `call`, `getBlockCount` | Fixture, otherwise `10008 RPC_ERROR` | Deterministic local result |
| `getBlock`, `getTransaction`, `getApplicationLog`, `getStorage`, `getTokenInfo` | Fixture or `10003 NOTFOUND` | Fixture or `10003 NOTFOUND` |

The provider exposes `extra.mock`, profile id, transaction mode, and session id. `window.__OneGateMock.getTrace()` returns at most 500 request, resolve, and reject records.

## Diagnostics

- If startup reports no browser, inspect discovery and provide a compatible executable path. Do not rewrite the DApp or introduce a proxy fallback.
- If the provider is missing, inspect runtime exceptions and confirm support for `Runtime.addBinding` and `Page.addScriptToEvaluateOnNewDocument`.
- If the DApp misses the initial ready event, dispatch `Neo.DapiProvider.request` with version `1.0`; the provider responds with `Neo.DapiProvider.ready`.
- If a website starts logged out, remember that the temporary browser profile does not inherit normal-browser cookies.
- If Origin differs, inspect the final URL for a DApp or identity-provider redirect.
