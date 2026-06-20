# OneGate Tools

Developer utilities for testing OneGate login requests.

Start the tool without startup arguments:

```powershell
dotnet run --project OneGate.Tools
```

The tool shows a main menu:

```text
1. Generate login QR code
2. Generate login deep link
3. Generate payment QR code
4. Generate neo payment deep link
0. Exit
```

Each function asks for its own values interactively. Press Enter to accept the shown defaults.
After each function finishes, press any key to return to the main menu.

QR generation writes `artifacts/login-qr.svg` by default and can also render a terminal QR code.
Deep link generation uses OneGate's `neoauth://wallet/authenticate` app-link flow. The deep link challenge follows NEP-33 and does not include a `callback` field; OneGate returns the result through the `dapp` identifier in the request URI.
After the deep link is generated, the tool asks whether to send it to Android, an iOS simulator, Windows, or not send it.
Payment QR generation writes `artifacts/payment-qr.svg` by default and encodes a `neo:` payment URI.
Payment deep link generation prints the same `neo:` payment URI and then asks whether to send it to Android, an iOS simulator, Windows, or not send it.

Device sending behavior:

- Android uses `adb shell am start`. The tool first checks PATH, then common Android SDK locations for `adb.exe`.
- iOS simulator uses `xcrun simctl openurl`.
- Windows uses the local protocol launcher.

For QR login requests, the default callback is `https://httpbin.org/post`; use a dedicated webhook URL if you need to inspect the signed login response.
The callback must be an absolute HTTPS URL and cannot be `localhost`, matching the current OneGate login callback validation.

Payment requests use this format:

```text
neo:{recipient-address}?asset={neo|gas|asset-hash}&amount={amount}
```

By default the tool uses recipient `NVg7LjGcUSrgxgjX3zEgqaksfMaiS8Z6e1`, `asset=neo`, and no amount.
