# P2-14: Explicit unsupported signer handling

Run the linked production-source harness:

```sh
dotnet run --project tests/p2-14/P2-14.Tests.csproj -p:NuGetAudit=false
```

The first 12 tests were written and executed before changing RpcClient: the old
implementation passed 2/12 and failed the others with null dereferences or the
wrong error. The fixed implementation passes all 13 cases, including an additional
multiple-local-signer order/scope regression case. The harness uses the actual
RpcClient, model and serializer sources, and the same Neo 3.10.0 package as the app.
Only unused UI diagnostics and MAUI filesystem directories are stubbed. The private
HTTP transport is replaced in tests with an in-memory handler: no real RPC calls,
wallet saves, private keys, signatures or broadcasts are performed.

External signers and watch-only accounts without a verification contract receive
NEP-21 UNSUPPORTED (10001) before script simulation. Missing wallets and absent
implicit payers receive NOT_FOUND (10003). Automatic payer selection skips
watch-only entries. Local known verification contracts remain usable for unsigned
transaction construction even without a private key; local multisig scripts and
signer order/scopes are retained.

This intentionally does not claim support for external multisig coordination or
deployed-contract witness construction. A signer hash is not enough to choose or
invent a verification script. Neo's [WalletAccount source](https://github.com/neo-project/neo/blob/33417b47238d331115cc0bd8cee93c1f2ed7a7f3/src/Neo/Wallets/WalletAccount.cs)
defines watch-only by the absence of a Contract, independently from HasKey; its
[fee calculation](https://github.com/neo-project/neo/blob/33417b47238d331115cc0bd8cee93c1f2ed7a7f3/src/Neo/Wallets/Helper.cs)
handles deployed-contract verification separately. Error codes follow
[NEP-21](https://github.com/neo-project/proposals/blob/master/nep-21.mediawiki).

Platform QA, on both iOS and Android: use an authorized test DApp/wallet to request
`makeTransaction` with a local payer and an external signer. Expect code 10001
with an explanatory message and no wallet confirmation or simulation RPC. Repeat
with a watch-only signer. A local-account-only `makeTransaction` should still
produce an unsigned context; do not sign or broadcast for this QA. Verify app
launch and ordinary account viewing. Simulator validation is not claimed by this
console harness.
