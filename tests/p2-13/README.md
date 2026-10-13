# NEP-21 query response regression tests

`getBlock` and `getTransaction` currently expose verbose node RPC JSON. This
regression harness links the production response mapper without loading MAUI or
opening a wallet. It uses the application's exact Neo 3.10.0 dependency to
generate node-shaped block and transaction responses.

## Contract

The mapper takes a `JsonObject` and the configured Neo address version, returning
a new object. The original RPC result is unchanged. Only these two dAPI query
methods use the mapper; application logs and invocation results are unchanged.

| Node RPC field | NEP-21 field | Conversion |
| --- | --- | --- |
| `sysfee`, `netfee` | `systemFee`, `networkFee` | Preserve atomic-unit integer strings exactly; no GAS scaling |
| `validuntilblock` | `validUntilBlock` | Preserve numeric value |
| `blockhash`, `blocktime` | `blockHash`, `blockTime` | Preserve values when supplied by the node |
| `sender` | `sender` | Decode address to UInt160 with the configured address version |
| `previousblockhash`, `merkleroot`, `nextblockhash` | `previousBlockHash`, `merkleRoot`, `nextBlockHash` | Preserve hashes; next hash remains optional |
| `nextconsensus` | `nextConsensus` | Decode address to UInt160 |
| signer `allowedcontracts`, `allowedgroups` | `allowedContracts`, `allowedGroups` | Preserve arrays |

All transactions nested under `block.tx` are converted, with `blockHash`,
`blockTime` and `confirmations` taken from the enclosing block. RPC does not add
these properties to nested transactions. An unconfirmed standalone transaction
has no mined-block metadata; leave it absent rather than fabricating a block or
timestamp. Extra node properties, including witnesses, are retained for existing
clients. Blockchain timestamps remain milliseconds; block nonce remains hex.

For example, `{"sysfee":"100000000","sender":"<N3 address>"}` becomes
`{"systemFee":"100000000","sender":"0x<40 hex digits>"}`. Clients that relied
on nonstandard lower-case RPC names should read the NEP-21 names instead.

Invalid addresses and malformed array types are rejected with standard .NET
format/invalid-operation exceptions; the existing dAPI RPC boundary remains responsible for
reporting errors. The mapper performs no network access, signing, state changes,
or caching. Work and additional memory are linear in the response size. No
configuration changes or database migration are required.

## Sources

- [NEP-21 types](https://github.com/neo-project/proposals/blob/master/nep-21.mediawiki)
- [Neo 3.10.0 Transaction.ToJson](https://github.com/neo-project/neo/blob/v3.10.0/src/Neo/Network/P2P/Payloads/Transaction.cs)
- [Neo 3.10.0 Header.ToJson](https://github.com/neo-project/neo/blob/v3.10.0/src/Neo/Network/P2P/Payloads/Header.cs)
- [Neo 3.10.0 Signer.ToJson](https://github.com/neo-project/neo/blob/v3.10.0/src/Neo/Network/P2P/Payloads/Signer.cs)
- [RPC block/transaction metadata](https://github.com/neo-project/neo-modules/blob/master/src/RpcServer/RpcServer.Blockchain.cs)
- [RPC fee serialization](https://github.com/neo-project/neo-modules/blob/master/src/RpcServer/Utility.cs)

## Validation and deployment

```sh
dotnet test tests/p2-13/OneGate.DapiQuery.Tests.csproj
dotnet test tests/p2-13/OneGate.DapiQuery.Tests.csproj --collect:"XPlat Code Coverage" --settings tests/p2-13/coverage.runsettings
```

Before committing, validate on both iOS and Android simulators: in a connected
DApp call `getBlock` by index and hash, read a nested transaction's camel-case
properties, then call `getTransaction` for its hash. Compare fee strings,
sender hashes, block metadata and signer restrictions. Verify the current tip
does not invent a `nextBlockHash`, and ordinary DApp queries still load normally.
Keep screenshots and simulator artifacts outside the repository. Deploy through
the normal OneGate app release after review; the harness is not part of the app.
