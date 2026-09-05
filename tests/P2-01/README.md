# P2-01 mnemonic word selection regression

Run `dotnet test tests/P2-01/MnemonicSelection.Tests.csproj`.

Tests execute the linked production VerifyMnemonicPage click handler using
synthetic words and lightweight visual doubles. They cover complete removal,
middle-word correction, repeated-word identity and complete selection order.
No real mnemonic or wallet is read or created.

Device check: create a disposable wallet, select a word, tap it again, then
reselect it. Repeat with a middle word and finish verification normally.
