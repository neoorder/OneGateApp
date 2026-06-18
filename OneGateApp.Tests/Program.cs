using NeoOrder.OneGate.Services;

Run("password policy rejects weak inputs", () =>
{
    AssertEqual(PasswordPolicyFailure.Required, PasswordPolicy.Evaluate(" ").Failure);
    AssertEqual(PasswordPolicyFailure.TooShort, PasswordPolicy.Evaluate("password").Failure);
    AssertEqual(PasswordPolicyFailure.LeadingOrTrailingWhitespace, PasswordPolicy.Evaluate(" N3o!Wallet-2026 ").Failure);
    AssertEqual(PasswordPolicyFailure.RepeatedPattern, PasswordPolicy.Evaluate("aaaaaaaaaa").Failure);
    AssertEqual(PasswordPolicyFailure.RepeatedPattern, PasswordPolicy.Evaluate("abcdef1234").Failure);
    AssertTrue(PasswordPolicy.Evaluate("N3o!Wallet-2026").IsValid);
});

Console.WriteLine("All OneGate P0 tests passed.");

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void AssertTrue(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
