# GameFi Asset Budget and CDN Cache Policy

This policy defines the publishing expectations for mobile web games listed in
OneGate. It is intentionally a developer and review contract first. OneGate can
surface diagnostics from these rules, but the app should not silently rewrite a
third-party game's DOM, CSS, assets, or runtime behavior.

## Goals

- Reduce blank-screen time for WebView games on Android and iOS.
- Make game submissions reviewable with concrete asset and cache data.
- Give game teams clear CDN rules before they are listed in OneGate.
- Keep enforcement explicit. Diagnostics may warn, but listing or runtime
  blocking requires a separate product decision.

## Scope

This policy applies to game dApps and GameFi experiences loaded through the
OneGate dApp runtime.

It does not change normal dApps, does not add app chrome, does not add a trust
bar, and does not adapt third-party game layout inside OneGate.

## Recommended Budgets

Budgets are measured after transport compression when possible. Teams should
report both compressed and uncompressed totals when submitting a game.

| Area | Target | Warning | Notes |
| --- | ---: | ---: | --- |
| HTML plus critical CSS and bootstrap JS | 750 KB | 1.5 MB | Enough to paint loading UI and start fetching the first scene. |
| Total first interactive load | 4 MB | 8 MB | Includes JS, WASM, textures, audio needed before first interaction. |
| JS plus WASM required before first interaction | 2 MB | 4 MB | Split optional engine modules and chain features into later chunks. |
| First-scene textures | 3 MB | 6 MB | Prefer WebP or AVIF for UI art and compressed GPU texture formats when available. |
| First-scene audio | 500 KB | 1 MB | Defer music packs and voice lines until after first interaction. |
| Largest single asset | 3 MB | 5 MB | Large files should have visible progress and retry behavior. |
| Time to visible loading UI on Wi-Fi simulator | 1 s | 2 s | The user should see game-owned progress, not a blank WebView. |
| Time to first interaction on Wi-Fi simulator | 5 s | 10 s | Heavier games must show progress and allow retry. |

These are review thresholds, not hard runtime limits. A game can exceed a
warning threshold if the game team documents why the asset is required and the
loading experience remains acceptable on Android and iOS.

## Required Loading Behavior

Every listed game should render its own loading state before large assets are
downloaded.

- Show a visible progress or staged loading state before loading the main bundle.
- Show a recoverable error state for CDN failures, timeout, unsupported WebGL,
  or failed wallet capability detection.
- Keep wallet connection deferred until the player takes an action that needs
  wallet access.
- Defer non-critical chain reads, analytics, social login, and promotional
  media until after the first interactive scene.
- Avoid blocking first paint on third-party scripts.

## CDN Cache Headers

Use hashed filenames for immutable assets and short-lived validation for entry
documents.

| Resource | Cache-Control | Extra requirements |
| --- | --- | --- |
| `index.html` | `no-cache` or `max-age=60, must-revalidate` | Must revalidate so catalog updates can take effect. |
| `onegate-game.json` | `no-cache` or `max-age=60, must-revalidate` | Must represent the current listed build. |
| Hashed JS/CSS/WASM/assets | `public, max-age=31536000, immutable` | Filename must include a content hash. |
| Service worker | `no-cache` | Avoid trapping old bridge behavior or stale manifest data. |
| API/config JSON | `no-cache` or a short explicit TTL | Do not cache wallet/session-specific responses as immutable assets. |

### Content Types

- JavaScript: `application/javascript`
- CSS: `text/css`
- WASM: `application/wasm`
- JSON manifests: `application/json`
- WebP: `image/webp`
- AVIF: `image/avif`

Incorrect content types can break WKWebView or Android WebView caching and
streaming behavior, especially for WASM.

## File Naming

- Use content-hashed filenames for immutable bundles, for example
  `game.ab12cd34.js`.
- Do not rely on query-string versioning for large immutable assets.
- Keep the entry URL stable and move versioning into hashed assets plus the
  optional game manifest metadata.
- Avoid CDN rewrites that serve different content under the same immutable URL.

## Manifest Linkage

When a game publishes an optional `onegate-game.json`, it should include the
same asset-budget values used for listing review. The OneGate app should treat
missing manifests as current behavior.

Recommended manifest fields for this policy:

- `assetBudget.initialBytes`
- `assetBudget.totalFirstLoadBytes`
- `assetBudget.jsWasmBytes`
- `assetBudget.textureBytes`
- `assetBudget.audioBytes`
- `version`
- `packageHash`

## Submission Checklist

Game submissions should include:

- Entry URL and manifest URL, if available.
- Largest ten first-load assets with compressed and uncompressed sizes.
- Total first interactive load size.
- CDN cache headers for `index.html`, manifest, JS, WASM, textures, and audio.
- Android and iOS screenshots showing loading, first interaction, and wallet
  connection state.
- Notes for any budget warning threshold that is exceeded.
- Confirmation that screenshots are uploaded as PR or issue assets and are not
  committed to the repository.

## Diagnostics Guidance

Future OneGate diagnostics may report these values when available:

- Entry URL and origin.
- Manifest URL and schema version.
- Declared asset budget.
- Observed navigation timing and first visible loading UI timing.
- WebView platform and app version.
- Last load error or timeout.

Diagnostics must not include private keys, seed data, wallet passwords, or
full transaction signing payloads.

## Non-goals

- No OneGate-side DOM or CSS adaptation for third-party games.
- No persistent runtime trust bar.
- No local transaction history database.
- No automatic runtime blocking solely because a warning budget is exceeded.
- No screenshots committed to the repository.
