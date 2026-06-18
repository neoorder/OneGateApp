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

Run("dapp permissions normalize and expire grants", () =>
{
    DateTimeOffset now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
    DAppPermissionGrant grant = DAppPermissions.CreateGrant("Example.COM.", 23, now);

    AssertEqual("example.com", grant.Host);
    AssertEqual("dapps/permissions/example.com", DAppPermissions.SettingsKeyForHost("Example.COM."));
    AssertTrue(DAppPermissions.IsFresh(grant, now.AddDays(29)));
    AssertFalse(DAppPermissions.IsFresh(grant, now.AddDays(31)));
    AssertFalse(DAppPermissions.IsFresh(grant, now.AddDays(-1)));
    AssertTrue(DAppPermissions.RequiresConnection("getAccounts"));
    AssertFalse(DAppPermissions.RequiresConnection("getBlock"));
    AssertFalse(ReferenceEquals(DAppPermissions.DefaultScopes, grant.Scopes));
});

Run("dapp permissions require exact same origin", () =>
{
    Uri trusted = new("https://example.com/app/index.html");

    AssertTrue(DAppPermissions.IsSameOrigin(trusted, new Uri(trusted, "/next?ok=1")));
    AssertFalse(DAppPermissions.IsSameOrigin(trusted, new("https://wallet.example.com/app/index.html")));
    AssertFalse(DAppPermissions.IsSameOrigin(trusted, new("http://example.com/app/index.html")));
    AssertFalse(DAppPermissions.IsSameOrigin(trusted, new("https://example.com:8443/app/index.html")));
});

await RunAsync("rpc endpoint pool fails over and pins healthy endpoint", async () =>
{
    AssertEqual(5, new RpcEndpointPool().Endpoints.Count);

    Uri first = new("https://bad.example");
    Uri second = new("https://good.example");
    RpcEndpointPool pool = new([first, second]);
    List<Uri> attempts = [];

    Uri result = await pool.SendAsync(endpoint =>
    {
        attempts.Add(endpoint);
        if (endpoint == first)
            throw new HttpRequestException("offline");
        return Task.FromResult(endpoint);
    });

    AssertEqual(second, result);
    AssertEqual(second, pool.PreferredEndpoint);
    AssertEqual(2, attempts.Count);
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

static async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
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

static void AssertFalse(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
