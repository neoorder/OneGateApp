# NEP-21 RPC error mapping regression harness

This independent .NET 10 console harness links the production `RpcServer`,
`RpcException`, `RpcMethodAttribute` and `DapiException`. It requires no mobile
workload, RPC server or wallet. Test-only serializer options and invocation data
isolate the RPC boundary from MAUI and Neo VM dependencies.

Run from the repository root:

```sh
dotnet run --project tests/p2-15/P2-15.csproj
```

The boundary keeps the JSON-RPC request ID and returns one result or one error.
Parameter conversion errors become NEP-21 INVALID (10002). Node exceptions become
RPC_ERROR (10008), with the original node code, message and data in `error.data`.
Timeout exceptions, including the timeout nested by HttpClient inside cancellation,
become TIMEOUT (10005); ordinary cancellation remains CANCELED (10006).
Reflection wrappers do not hide business errors. Unexpected handler errors remain
UNKNOWN (10000), including handler bugs that happen to throw a format exception.

For example, a node error `{ "code": -100, "message": "Unknown transaction",
"data": { "hash": "0x123" } }` becomes:

```json
{
  "jsonrpc": "2.0",
  "id": "request-7",
  "error": {
    "code": 10008,
    "message": "Unknown transaction",
    "data": {
      "code": -100,
      "message": "Unknown transaction",
      "data": { "hash": "0x123" }
    }
  }
}
```

The mapping does not retry requests, sign transactions, or introduce new network
calls. Exception work is bounded by the wrapper depth; successful calls retain
the existing dispatch path. Node error data is copied into the response, so it
does not reparent/mutate JSON owned by the RPC client. No deployment or data
migration is required. Clients can now branch on the documented NEP-21 codes
instead of treating these known failures as UNKNOWN.

Simulator QA should call `getTransaction` with a malformed hash (INVALID), a
well-formed nonexistent hash (RPC_ERROR with nested node error), and cancel an
address-selection dialog (CANCELED). Test a deliberately stalled test endpoint
for TIMEOUT where the simulator environment supports endpoint fixtures. Use no
real signing/broadcast requests for this validation.
