using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

Console.OutputEncoding = Encoding.UTF8;

while (true)
{
    ClearScreen();
    Console.WriteLine("OneGate Tools");
    Console.WriteLine();
    Console.WriteLine("1. Generate login QR code");
    Console.WriteLine("2. Generate login deep link");
    Console.WriteLine("0. Exit");
    Console.WriteLine();
    Console.Write("Select a function: ");

    string? input = Console.ReadLine();
    if (input is null && Console.IsInputRedirected)
        return 0;

    string? choice = input?.Trim();
    if (choice == "0")
        return 0;

    try
    {
        switch (choice)
        {
            case "1":
                GenerateQrCode();
                break;
            case "2":
                GenerateDeepLink();
                break;
            default:
                Console.WriteLine("Unknown selection.");
                break;
        }
    }
    catch (Exception ex) when (ex is ArgumentException or System.FormatException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
    }

    WaitForAnyKey();
}

static void GenerateQrCode()
{
    ClearScreen();
    Console.WriteLine("Generate login QR code");
    Console.WriteLine();

    ChallengeOptions challenge = ReadQrChallengeOptions();
    string outputPath = ReadText("SVG output path", ToolDefaults.Output);
    int size = ReadInt("SVG size in pixels", 420, min: 128);
    bool renderTerminalQr = ReadYesNo("Render terminal QR code", defaultValue: true);

    AuthenticationChallengePayload payload = CreatePayload(challenge);
    string json = JsonSerializer.Serialize(payload, JsonOptions.Default);
    string fullOutputPath = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
    File.WriteAllText(fullOutputPath, CreateSvg(json, size), Encoding.UTF8);

    Console.WriteLine();
    WritePayloadSummary(payload);
    Console.WriteLine($"SVG:       {fullOutputPath}");
    Console.WriteLine();

    if (renderTerminalQr)
    {
        RenderTerminalQr(json);
        Console.WriteLine();
    }

    Console.WriteLine("Payload JSON:");
    Console.WriteLine(json);
}

static void GenerateDeepLink()
{
    ClearScreen();
    Console.WriteLine("Generate login deep link");
    Console.WriteLine();

    DeepLinkOptions options = ReadDeepLinkOptions();
    AuthenticationChallengePayload payload = CreatePayload(options.Challenge);
    string json = JsonSerializer.Serialize(payload, JsonOptions.Default);
    string deepLink = CreateDeepLink(json, options);

    Console.WriteLine();
    WritePayloadSummary(payload);
    Console.WriteLine($"DApp:      {options.DAppIdentifier}");
    Console.WriteLine($"Host:      {options.DeepLinkHost}");
    Console.WriteLine();
    Console.WriteLine("Deep link:");
    Console.WriteLine(deepLink);
    Console.WriteLine();
    Console.WriteLine("Payload JSON:");
    Console.WriteLine(json);
    Console.WriteLine();

    SendTarget? target = ReadDeepLinkSendTarget();
    if (target is null)
        return;

    string? device = ReadSendDevice(target.Value);
    SendDeepLink(deepLink, target.Value, device);
    Console.WriteLine($"Sent deep link through {FormatSendTarget(target.Value)}.");
}

static ChallengeOptions ReadQrChallengeOptions()
{
    string domain = ReadRequiredText("Domain shown in OneGate", ToolDefaults.Domain);
    string callback = ReadHttpsCallback();
    uint network = ReadUInt32("Neo network magic", ToolDefaults.Network);
    return new ChallengeOptions(domain, callback, network);
}

static ChallengeOptions ReadDeepLinkChallengeOptions()
{
    string domain = ReadRequiredText("Domain shown in OneGate", ToolDefaults.Domain);
    uint network = ReadUInt32("Neo network magic", ToolDefaults.Network);
    return new ChallengeOptions(domain, null, network);
}

static DeepLinkOptions ReadDeepLinkOptions()
{
    ChallengeOptions challenge = ReadDeepLinkChallengeOptions();
    string dapp = ReadDAppIdentifier();
    string host = ReadDeepLinkHost();
    return new DeepLinkOptions(challenge, dapp, host);
}

static string ReadHttpsCallback()
{
    while (true)
    {
        string value = ReadText("HTTPS callback URL", ToolDefaults.Callback);
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        Console.WriteLine("Callback must be an absolute HTTPS URL and cannot be localhost.");
    }
}

static string ReadDAppIdentifier()
{
    while (true)
    {
        string value = ReadText("DApp identifier", ToolDefaults.DAppIdentifier);
        if (IsValidDAppIdentifier(value))
            return value;

        Console.WriteLine("DApp identifier must look like a reversed domain, use lowercase letters and numbers, and contain at least one dot.");
    }
}

static string ReadDeepLinkHost()
{
    while (true)
    {
        string value = ReadText("Deep link host (wallet/onegate.space)", ToolDefaults.DeepLinkHost);
        if (value is "wallet" or ToolDefaults.OneGateDomain)
            return value;

        Console.WriteLine("Deep link host must be wallet or onegate.space.");
    }
}

static string ReadRequiredText(string label, string defaultValue)
{
    while (true)
    {
        string value = ReadText(label, defaultValue);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        Console.WriteLine($"{label} cannot be empty.");
    }
}

static string ReadText(string label, string defaultValue)
{
    Console.Write($"{label} [{defaultValue}]: ");
    string? value = Console.ReadLine();
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
}

static string? ReadOptionalText(string label)
{
    Console.Write($"{label}: ");
    string? value = Console.ReadLine();
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

static SendTarget? ReadDeepLinkSendTarget()
{
    while (true)
    {
        Console.WriteLine("Send deep link?");
        Console.WriteLine("0. Do not send");
        Console.WriteLine("1. Android device");
        Console.WriteLine("2. iOS simulator");
        Console.WriteLine("3. Windows");
        Console.Write("Select a target [0]: ");

        string? value = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(value) || value == "0")
            return null;

        switch (value)
        {
            case "1":
                return SendTarget.Android;
            case "2":
                return SendTarget.IosSimulator;
            case "3":
                return SendTarget.Windows;
            default:
                Console.WriteLine("Unknown send target.");
                break;
        }
    }
}

static string? ReadSendDevice(SendTarget target)
{
    return target switch
    {
        SendTarget.Android => ReadOptionalText("Android device serial (empty for adb default)"),
        SendTarget.IosSimulator => ReadText("iOS simulator UDID/name", "booted"),
        _ => null
    };
}

static bool ReadYesNo(string label, bool defaultValue)
{
    string suffix = defaultValue ? "Y/n" : "y/N";
    while (true)
    {
        Console.Write($"{label} [{suffix}]: ");
        string? value = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(value))
            return defaultValue;
        if (value.Equals("y", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("n", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        Console.WriteLine("Enter y or n.");
    }
}

static int ReadInt(string label, int defaultValue, int min)
{
    while (true)
    {
        string value = ReadText(label, defaultValue.ToString(CultureInfo.InvariantCulture));
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) && result >= min)
            return result;

        Console.WriteLine($"{label} must be at least {min}.");
    }
}

static uint ReadUInt32(string label, uint defaultValue)
{
    while (true)
    {
        string value = ReadText(label, defaultValue.ToString(CultureInfo.InvariantCulture));
        try
        {
            return ParseUInt32(value);
        }
        catch (System.FormatException)
        {
            Console.WriteLine($"{label} must be a decimal number or 0x-prefixed hexadecimal number.");
        }
        catch (OverflowException)
        {
            Console.WriteLine($"{label} is outside the UInt32 range.");
        }
    }
}

static uint ParseUInt32(string value)
{
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return uint.Parse(value, CultureInfo.InvariantCulture);
}

static AuthenticationChallengePayload CreatePayload(ChallengeOptions options)
{
    return new AuthenticationChallengePayload
    {
        Domain = options.Domain,
        Networks = [options.Network],
        Nonce = CreateNonce(),
        Timestamp = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        Callback = options.Callback is null ? null : new Uri(options.Callback)
    };
}

static ulong CreateNonce()
{
    Span<byte> bytes = stackalloc byte[sizeof(ulong)];
    RandomNumberGenerator.Fill(bytes);
    return BitConverter.ToUInt64(bytes);
}

static string CreateDeepLink(string json, DeepLinkOptions options)
{
    string host = Uri.EscapeDataString(options.DeepLinkHost);
    string dapp = Uri.EscapeDataString(options.DAppIdentifier);
    string payload = Uri.EscapeDataString(json);
    return $"neoauth://{host}/authenticate?dapp={dapp}&payload={payload}";
}

static string CreateSvg(string content, int size)
{
    var writer = new BarcodeWriterSvg
    {
        Format = BarcodeFormat.QR_CODE,
        Options = new QrCodeEncodingOptions
        {
            CharacterSet = "UTF-8",
            ErrorCorrection = ErrorCorrectionLevel.M,
            Height = size,
            Margin = 4,
            Width = size
        }
    };

    return writer.Write(content).Content;
}

static void RenderTerminalQr(string content)
{
    BitMatrix matrix = CreateQrMatrix(content, margin: 2);
    const string color = "\u001b[30;47m";
    const string reset = "\u001b[0m";

    Console.Write(color);
    for (int y = 0; y < matrix.Height; y += 2)
    {
        for (int x = 0; x < matrix.Width; x++)
        {
            bool top = matrix[x, y];
            bool bottom = y + 1 < matrix.Height && matrix[x, y + 1];
            Console.Write((top, bottom) switch
            {
                (false, false) => ' ',
                (true, false) => '▀',
                (false, true) => '▄',
                _ => '█'
            });
        }

        Console.Write(reset);
        Console.WriteLine();
        Console.Write(color);
    }

    Console.Write(reset);
}

static BitMatrix CreateQrMatrix(string content, int margin)
{
    var writer = new QRCodeWriter();
    Dictionary<EncodeHintType, object> hints = new()
    {
        [EncodeHintType.CHARACTER_SET] = "UTF-8",
        [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
        [EncodeHintType.MARGIN] = margin
    };
    return writer.encode(content, BarcodeFormat.QR_CODE, 0, 0, hints);
}

static void SendDeepLink(string deepLink, SendTarget target, string? device)
{
    switch (target)
    {
        case SendTarget.Android:
            SendAndroidDeepLink(deepLink, device);
            break;
        case SendTarget.IosSimulator:
            SendIosSimulatorDeepLink(deepLink, device);
            break;
        case SendTarget.Windows:
            Process.Start(new ProcessStartInfo
            {
                FileName = deepLink,
                UseShellExecute = true
            });
            break;
        default:
            throw new InvalidOperationException($"Unsupported send target: {target}.");
    }
}

static void SendAndroidDeepLink(string deepLink, string? device)
{
    string adbPath = ResolveExecutablePath("adb", GetAndroidAdbCandidates(), "Android Debug Bridge. Install Android platform-tools or add adb.exe to PATH.");
    List<string> arguments = [];
    if (!string.IsNullOrWhiteSpace(device))
    {
        arguments.Add("-s");
        arguments.Add(device);
    }
    arguments.AddRange(["shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", QuoteForAdbShell(deepLink)]);
    RunProcess(adbPath, arguments);
}

static void SendIosSimulatorDeepLink(string deepLink, string? device)
{
    string xcrunPath = ResolveExecutablePath("xcrun", [], "Xcode command line tools are required for iOS simulator deep links.");
    RunProcess(xcrunPath, ["simctl", "openurl", string.IsNullOrWhiteSpace(device) ? "booted" : device, deepLink]);
}

static string QuoteForAdbShell(string value)
{
    return "'" + value.Replace("'", "'\\''") + "'";
}

static void RunProcess(string fileName, IReadOnlyList<string> arguments)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
    };
    foreach (string argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Unable to start {fileName}.");
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(output))
        Console.WriteLine(output.Trim());

    if (process.ExitCode != 0)
    {
        string detail = string.IsNullOrWhiteSpace(error) ? output : error;
        throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}. {detail}".Trim());
    }
}

static string ResolveExecutablePath(string commandName, IEnumerable<string> candidatePaths, string installHint)
{
    string executableName = OperatingSystem.IsWindows() && !commandName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? commandName + ".exe"
        : commandName;

    string? pathMatch = FindExecutableOnPath(executableName);
    if (pathMatch is not null)
        return pathMatch;

    foreach (string candidate in candidatePaths)
    {
        if (File.Exists(candidate))
            return candidate;
    }

    throw new InvalidOperationException($"Unable to find {executableName}. {installHint}");
}

static string? FindExecutableOnPath(string executableName)
{
    string? path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
        return null;

    foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        try
        {
            string candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
                return candidate;
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    return null;
}

static IEnumerable<string> GetAndroidAdbCandidates()
{
    foreach (string sdkRoot in GetAndroidSdkRoots())
        yield return Path.Combine(sdkRoot, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
}

static IEnumerable<string> GetAndroidSdkRoots()
{
    string? androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
    if (!string.IsNullOrWhiteSpace(androidHome))
        yield return androidHome;

    string? androidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
    if (!string.IsNullOrWhiteSpace(androidSdkRoot))
        yield return androidSdkRoot;

    string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    if (!string.IsNullOrWhiteSpace(localAppData))
        yield return Path.Combine(localAppData, "Android", "Sdk");

    string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
    if (!string.IsNullOrWhiteSpace(programFilesX86))
        yield return Path.Combine(programFilesX86, "Android", "android-sdk");

    string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
    if (!string.IsNullOrWhiteSpace(programFiles))
        yield return Path.Combine(programFiles, "Android", "android-sdk");
}

static bool IsValidDAppIdentifier(string value)
{
    return Regex.IsMatch(value, @"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$", RegexOptions.CultureInvariant);
}

static void WritePayloadSummary(AuthenticationChallengePayload payload)
{
    Console.WriteLine("Request:");
    Console.WriteLine($"Domain:    {payload.Domain}");
    Console.WriteLine($"Network:   {payload.Networks[0]}");
    if (payload.Callback is not null)
        Console.WriteLine($"Callback:  {payload.Callback}");
    Console.WriteLine($"Timestamp: {payload.Timestamp}");
    Console.WriteLine("Valid for: 5 minutes from timestamp");
}

static string FormatSendTarget(SendTarget target)
{
    return target switch
    {
        SendTarget.Android => "Android adb",
        SendTarget.IosSimulator => "iOS simulator",
        SendTarget.Windows => "Windows protocol launcher",
        _ => target.ToString()
    };
}

static void WaitForAnyKey()
{
    Console.WriteLine();
    Console.Write("Press any key to return to the main menu...");
    if (Console.IsInputRedirected)
        Console.ReadLine();
    else
        Console.ReadKey(intercept: true);
}

static void ClearScreen()
{
    try
    {
        Console.Clear();
    }
    catch (IOException)
    {
        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine();
    }
}

enum SendTarget
{
    Android,
    IosSimulator,
    Windows
}

sealed record ChallengeOptions(string Domain, string? Callback, uint Network);

sealed record DeepLinkOptions(ChallengeOptions Challenge, string DAppIdentifier, string DeepLinkHost);

sealed class AuthenticationChallengePayload
{
    public string Action { get; init; } = "Authentication";

    [JsonPropertyName("grant_type")]
    public string GrantType { get; init; } = "Signature";

    [JsonPropertyName("allowed_algorithms")]
    public string[] AllowedAlgorithms { get; init; } = ["ECDSA-P256"];

    public required string Domain { get; init; }

    public required uint[] Networks { get; init; }

    public required ulong Nonce { get; init; }

    public required uint Timestamp { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Callback { get; init; }
}

static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

static class ToolDefaults
{
    public const uint Network = 860_833_102;
    public const string Callback = "https://httpbin.org/post";
    public const string Domain = "login-test.onegate.space";
    public const string DAppIdentifier = "tools.onegate.space";
    public const string DeepLinkHost = "wallet";
    public const string OneGateDomain = "onegate.space";
    public const string Output = "artifacts/login-qr.svg";
}
