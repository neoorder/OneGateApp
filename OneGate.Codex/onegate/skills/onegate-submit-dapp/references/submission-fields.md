# OneGate listing request fields

Canonical repository: `neoorder/OneGateApp`

Canonical form: `https://github.com/neoorder/OneGateApp/issues/new?template=dapp_submission.yml`

Issue title: `[DApp Listing]: <Project name>`

The public form applies the `enhancement` label. Do not fail an otherwise successful API or CLI submission merely because the submitter cannot apply that label.

## Required fields

Use these headings and preserve their order in a manually created issue body:

1. `Project name`
2. `Listing type`: exactly one of `DApp`, `Game`, `Tool`, or `Other`
3. `Short introduction`
4. `Launch URL`: exact public HTTPS URL OneGate should open
5. `Official website`: canonical public HTTPS project or organization site
6. `Icon URL`: public square app icon, preferably 512x512 PNG or WebP
7. `Developer or publisher`
8. `Contact information`: public email, Discord, Telegram, X/Twitter, or another public maintainer contact
9. `Category and tags`
10. `Supported languages`: language tags such as `en`, `zh-Hans`, or `zh-Hant`
11. `Supported networks`: one or more of `Neo N3 MainNet`, `Neo N3 TestNet`, `Neo X MainNet`, `Neo X TestNet`, or `Other`
12. `Wallet integration`: one or more of `OneGate dAPI`, `NeoLine-compatible provider`, `WalletConnect`, `No wallet connection required`, or `Other`
13. `Wallet permissions and methods`
14. `Mobile readiness`: exactly one of `Verified on iOS and Android mobile WebViews`, `Responsive mobile browser support`, `Desktop-first, mobile improvements planned`, or `Not sure`
15. `Content and risk level`: exactly one of `General audience`, `Financial or trading activity`, `Game`, `Restricted or age-gated content`, or `Other`

## Optional fields

- `Preview assets`: one public screenshot, video, or preview URL per line
- `Mobile validation notes`: tested devices, browsers, WebViews, known layout limits, and wallet-flow reproduction notes
- `Security and compliance notes`: audits, source repository, contract hashes, third-party scripts, tracking, privacy, or content restrictions
- `Additional context`

## Game-specific fields

Treat these as required for a `Game` listing. For other listing types, omit them or set `Game runtime preference` to `Not a game` only when useful for clarity.

- `Game manifest URL`: public `onegate-game.json` or equivalent, or explicitly `None`
- `Game runtime preference`: `Standard DApp WebView`, `Fullscreen game runtime`, `Landscape preferred`, `Portrait preferred`, or `Other`
- `Game resource and performance budget`: first-load size, major JS/WASM/audio/texture assets, expected FPS, memory expectations, and cache strategy
- `Game wallet and settlement flow`: wallet-connect timing, signing and transaction methods, and off-chain/on-chain settlement
- `Game licensing and brand rights`: art, audio, brands, ROM/emulator content, third-party IP, and user-generated-content permissions or policies

## Confirmation text

The submitter must explicitly confirm every statement before publication:

- I confirm the submitted URLs are official project URLs or I have permission to request this listing.
- I understand OneGate may reject, delay, or remove listings for security, quality, legal, App Store, or user-experience reasons.
- I confirm this issue does not include private keys, seed phrases, credentials, or other secrets.

Render these as unchecked boxes in a draft. Change them to checked boxes only after the user explicitly confirms them.

## Markdown body shape

Render scalar values as plain paragraphs and multi-select values as bullet lists. Use this structure, adding optional and game-specific sections only when relevant:

```markdown
### Project name
<value>

### Listing type
<value>

### Short introduction
<value>

### Launch URL
<value>

### Official website
<value>

### Icon URL
<value>

### Preview assets
<value or Not provided>

### Developer or publisher
<value>

### Contact information
<value>

### Category and tags
<value>

### Supported languages
<value>

### Supported networks
- <value>

### Wallet integration
- <value>

### Wallet permissions and methods
<value>

### Mobile readiness
<value>

### Mobile validation notes
<value or Not provided>

<!-- For a Game, insert the five game-specific headings here. -->

### Content and risk level
<value>

### Security and compliance notes
<value or Not provided>

### Additional context
<value or Not provided>

### Submitter confirmation
- [ ] I confirm the submitted URLs are official project URLs or I have permission to request this listing.
- [ ] I understand OneGate may reject, delay, or remove listings for security, quality, legal, App Store, or user-experience reasons.
- [ ] I confirm this issue does not include private keys, seed phrases, credentials, or other secrets.
```
