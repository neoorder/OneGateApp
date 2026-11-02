# OneGate

OneGate is a cross-platform wallet for the **Neo N3** ecosystem, built with **C#**, **XAML**, and **.NET MAUI 10**.
Create and import wallets, manage tokens and NFTs, use dApps, and chat through Meshline.

**Platforms:** Android · iOS · macOS (Mac Catalyst) · Windows

[Changelog](OneGateApp/CHANGELOG.md) · [Development guide](CONTRIBUTING.md) · [DApp tools](OneGate.Tools/README.md) · [Codex plugin](OneGate.Codex/README.md)

## Features

- Create wallets locally or import a mnemonic, WIF, raw private key, or NEP-2 encrypted key.
- Manage NEO, GAS, and other NEP-17 assets; view and transfer NEP-11 NFTs.
- Launch dApps with wallet authorization and Neo N3 dAPI support.
- Handle payment and authentication requests through QR codes and `neo:` / `neoauth:` links.
- Keep an address book and use encrypted chat through Meshline, with contacts and account profiles.
- Protect sensitive operations with biometrics and screen-security handling.
- Use opt-in developer tools for encrypted remote DApp debugging.
- Choose from 15 interface languages and light or dark themes.

## Build from source

Install the .NET 10 SDK, .NET MAUI workloads, and the platform tools for your target.
Open [OneGateApp.slnx](OneGateApp.slnx), select `OneGateApp` as the startup project, and choose a device or emulator.
iOS and macOS development require a Mac with a compatible Xcode installation.

For example, to build Android from the repository root:

```sh
dotnet build OneGateApp/OneGateApp.csproj -f net10.0-android
```

See the [development guide](CONTRIBUTING.md) for platform setup, signing, dependencies, and validation.

## Protocol support

OneGate is built for the Neo N3 ecosystem and includes support or integration points for:

- **NEP-2** — encrypted private key import / export
- **NEP-6** — Neo wallet file compatibility
- **NEP-9** — `neo:` URI handling
- **NEP-11** — NFT display and transfer
- **NEP-17** — fungible token support
- **NEP-20** — authentication scheme
- **NEP-21** — dAPI for Neo N3 dApps
- **NEP-33** — DApp authentication URI flow

## Languages

English · 简体中文 · 繁體中文 · Deutsch · Español · Français · Bahasa Indonesia · Italiano · 日本語 · 한국어 · Nederlands · Português (Brasil) · Русский · Türkçe · Tiếng Việt

## Contributing

Issues and pull requests are welcome. Contributions to platform support, UI, translations, wallet interoperability,
dApp integration, tests, and documentation are appreciated. For larger changes, open an issue first.

Read [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and validation guidance.

## License

[MIT](LICENSE.txt)
