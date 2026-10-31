# P2-03 consistent authorization cancellation

Run `dotnet test tests/P2-03/AuthorizationCancellation.Tests.csproj`.

Tests link the production WalletAuthorizationService and use Neo 3.10.0 to
create disposable locked/unlocked wallets. Platform prompt doubles inject user
cancellation, rejection, success and a separate hardware error. No credentials
or real wallets are accessed. This verifies cancellation semantics rather than
claiming that a desktop test displays the native biometric prompt.

On both mobile simulators: use a disposable wallet and enrolled test biometrics,
authorize once, then cancel another transfer/export authorization. Cancellation
must return to the previous screen without a signature, export or crash.
Repeat after a cold start while the wallet is locked. Real native cancellation
and both caller flows are required before submission.
