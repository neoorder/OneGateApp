# Phaser and Pixi GameFi Starter Guide

This guide defines a lightweight starter architecture for mobile-first GameFi
dApps that run inside OneGate. It is a template guide, not a committed game
project, so the OneGate app repository does not need to carry large JavaScript
dependencies or generated assets.

## Recommended Stack

Use a normal web game stack and keep the OneGate integration thin.

| Layer | Recommended choice | Notes |
| --- | --- | --- |
| Build tool | Vite | Fast local dev, predictable production bundles. |
| Language | TypeScript | Required for wallet payloads, asset manifests, and SDK integration. |
| Game engine | Phaser or PixiJS | Phaser for complete 2D game structure; PixiJS for custom render loops and UI-heavy games. |
| UI overlay | Plain DOM or lightweight framework | Keep wallet dialogs and native prompts outside the canvas when possible. |
| Wallet bridge | OneGate Game SDK | Feature-detect optional native capabilities before rendering dependent UI. |
| Assets | Texture atlases plus WebP/AVIF | Keep first-scene assets small and lazy-load later levels. |
| Hosting | CDN with hashed assets | Follow the GameFi asset budget and cache policy. |

## Phaser vs PixiJS

Choose Phaser when the game needs scenes, physics, tilemaps, input systems, and
asset loading conventions.

Choose PixiJS when the game is mostly custom rendering, animation, slot-like
UI, collectible display, or a small arcade loop that does not need Phaser's
full scene framework.

Avoid shipping desktop-first Unity/WebGL builds unless the game has a strong
reason and has been profiled inside Android WebView and iOS WKWebView.

## Project Layout

```text
game/
  index.html
  public/
    onegate-game.json
    icons/
  src/
    main.ts
    onegate.ts
    scenes/
      BootScene.ts
      PreloadScene.ts
      GameScene.ts
    ui/
      wallet.ts
    assets.ts
  package.json
  tsconfig.json
  vite.config.ts
```

Keep `index.html` tiny. It should paint a loading shell immediately and then
load the main script.

## Entry HTML

```html
<main id="game-root">
  <section id="loading">
    <strong>Loading</strong>
    <progress max="100" value="0"></progress>
  </section>
</main>
<script type="module" src="/src/main.ts"></script>
```

Do not leave the WebView blank while large assets download.

## OneGate Integration

Create a small integration module that waits for the provider and exposes
feature-detected helpers to the game.

```ts
import {
  createOneGateGameClient,
  type OneGateGameClient
} from "@onegate/game-sdk";

let onegate: OneGateGameClient | undefined;

export async function getOneGate() {
  onegate ??= await createOneGateGameClient({ timeoutMs: 5000 });
  return onegate;
}

export async function chooseAddress() {
  const client = await getOneGate();
  if (!client.hasCapability("pickAddress")) {
    return undefined;
  }
  return client.pickAddress("Choose a recipient");
}
```

Wallet calls should be tied to player intent. Do not call `authenticate`,
`send`, `invoke`, `sign`, or `signMessage` during page load.

## Phaser Bootstrap

```ts
import Phaser from "phaser";
import { BootScene } from "./scenes/BootScene";
import { PreloadScene } from "./scenes/PreloadScene";
import { GameScene } from "./scenes/GameScene";

new Phaser.Game({
  type: Phaser.AUTO,
  parent: "game-root",
  backgroundColor: "#0b0f17",
  scale: {
    mode: Phaser.Scale.RESIZE,
    autoCenter: Phaser.Scale.CENTER_BOTH
  },
  input: {
    activePointers: 3
  },
  render: {
    antialias: false,
    pixelArt: false,
    roundPixels: true
  },
  scene: [BootScene, PreloadScene, GameScene]
});
```

Use `Phaser.Scale.RESIZE` and design the camera/layout for portrait and
landscape breakpoints. Do not hardcode desktop canvas dimensions.

## PixiJS Bootstrap

```ts
import { Application } from "pixi.js";

const app = new Application();

await app.init({
  resizeTo: window,
  background: "#0b0f17",
  antialias: false,
  autoDensity: true,
  resolution: Math.min(window.devicePixelRatio, 2)
});

document.getElementById("game-root")!.appendChild(app.canvas);
```

Clamp effective resolution on mobile. Rendering at full high-DPI resolution can
burn GPU and memory without visible benefit.

## Loading and Asset Strategy

- Paint loading UI before fetching the main game bundle.
- Load only the first scene before first interaction.
- Move later levels, music packs, videos, and chain-heavy features behind lazy
  imports.
- Use texture atlases for small sprites and UI elements.
- Prefer WebP/AVIF for static art and compressed texture formats where the
  engine and platform support them.
- Keep audio short and lazy-load background music.
- Show retry UI when a CDN asset fails.

## Wallet and Settlement Pattern

For most games, local play should not depend on immediate chain settlement.

Recommended flow:

1. Let the user start playing without wallet prompts when possible.
2. Ask for wallet/authentication only when the player chooses a chain action.
3. Preview the action in game UI before calling OneGate dAPI.
4. Call OneGate `send`, `invoke`, or `signMessage`.
5. Wait for OneGate confirmation and show pending/confirmed/failed state.
6. Reconcile game state from RPC or backend confirmation.

Do not store private keys, seed phrases, wallet passwords, or full signing
payloads in game storage.

## Runtime Events

Games should pause expensive loops when the WebView is hidden or backgrounded.

```ts
const onegate = await getOneGate();

const disposePause = onegate.onRuntimeEvent("runtimepause", () => {
  game.pause();
});

const disposeResume = onegate.onRuntimeEvent("runtimeresume", () => {
  game.resume();
});
```

Always keep a browser fallback through `visibilitychange`, `pagehide`, and
`pageshow` because older OneGate versions may not expose runtime events.

## Mobile QA Checklist

- First visible loading UI appears quickly.
- First interaction works in portrait and landscape.
- Touch targets are usable with one hand.
- Canvas resizes correctly after rotation and app resume.
- Audio pauses/resumes cleanly.
- Wallet actions are triggered only by user interaction.
- Optional native capabilities are hidden when not available.
- Android WebView and iOS WKWebView are both tested before listing.

## Non-goals

- No OneGate-side per-game DOM or CSS adaptation.
- No assumption that every OneGate version exposes every optional capability.
- No desktop-first fixed-size canvas.
- No automatic wallet prompts on load.
- No committed generated game assets in the OneGate app repository.
