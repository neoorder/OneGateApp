# P2-02 precise localized token amounts

Run `dotnet test tests/P2-02/TokenAmount.Tests.csproj`.

The tests link the production parser/formatter. Transfer units use BigInteger
throughout; decimal is only an optional fiat approximation in SendPage. Local
input accepts the current language's decimal separator, with no grouping or
scientific notation. Protocol-supplied amounts are localized at the input
boundary. PaymentAction and AppLinkAction are also linked: real protocol parsing
and route-query creation must retain invariant decimal strings, including values
outside System.Decimal's precision/range. Navigation alone is doubled. QR scan
routing preserves that same string. There are no wallet, RPC or network operations
in these tests.

Raw amount input is limited to 1024 characters before trimming or numeric
parsing, including URI/QR amounts. Tests accept the exact boundary, reject 1025
and 16384 characters (including otherwise valid leading-zero amounts), and keep
60-decimal-place values exact. The follow-up regression failed five cases before
adding the early length guard.

Before the fix, a temporary characterization adapter using the audited
`decimal.Parse(BigDecimal.ToString())` and `BigDecimal.TryParse` expressions
reproduced the German `1.5 -> 15` error, French parsing failure, local minimum
unit rejection and large-balance overflow. The adapter was removed afterwards.
The separate production PaymentAction regression initially failed 8 of its 12
cases before removing the current-culture `decimal.Parse` conversion.

Device checks: open a send form with a synthetic 1.5 token balance, select
English/German/French and tap All. Expect 1.5/1,5/1,5 and the same integer
150000000 at 8 decimals. Check the smallest unit, overprecision, zero and values
above balance. Entering an amount does not authorize or broadcast a transfer.
Also pass a real `PaymentAction` query into SendPage under each locale, including
a large amount and a fraction smaller than System.Decimal can retain, to confirm
the URI/QR boundary never scales or rounds the amount before token validation.
