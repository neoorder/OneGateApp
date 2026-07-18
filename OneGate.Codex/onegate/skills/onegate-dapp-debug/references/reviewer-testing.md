# Self-contained reviewer check

The skill bundles a minimal DApp under `assets/reviewer-fixture`. It needs no checkout-specific server, public deployment, wallet balance, or RPC endpoint.

From the skill directory, run the platform launcher with `review start --headless`. If browser discovery needs help, repeat with `--browser-executable <absolute-path>`. Do not assume a browser brand.

The success envelope returns a `sessionId`, the temporary `reviewerFixture.url`, the preserved `origin`, `mock: true`, provider name `OneGate Codex Plugin`, the persistent development address, and `transactionMode: offline`. The fixture itself exercises:

- document-start provider discovery;
- account and zero-balance reads;
- persistent-account message signing;
- expected `10007` offline transfer rejection;
- expected `10008` offline RPC rejection;
- trace and browser-console capture.

Poll the fixture result with:

```text
<launcher> session evaluate --id <session-id> --expression "window.__fixtureResult || null"
```

A successful result has `mock: true`, `providerName: "OneGate Codex Plugin"`, `signatureLength: 88`, `sendCode: 10007`, `rpcCode: 10008`, and no `error` property. Inspect the full request lifecycle with `session trace` and `session logs`.

Always finish with:

```text
<launcher> session stop --id <session-id>
```

The temporary browser profile and reviewer HTTP server are removed when the session stops. The development identity remains for later signing tests by design.
