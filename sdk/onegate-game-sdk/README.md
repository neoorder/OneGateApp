# OneGate Game SDK

Small TypeScript helpers for GameFi dApps running inside OneGate.

The SDK is a thin wrapper over OneGate's injected dAPI provider and optional
native game capabilities. It does not create new wallet authorization semantics:
`authenticate` remains an explicit website authentication call, and wallet
signing or sending still goes through the existing OneGate confirmation flows.

## Install

This package is currently kept in the OneGate repository as source. A game can
copy it into a workspace or consume it after the package is published.

```bash
npm install @onegate/game-sdk
```

## Basic usage

```ts
import { createOneGateGameClient } from "@onegate/game-sdk";

const onegate = await createOneGateGameClient();
const accounts = await onegate.getAccounts();

if (onegate.hasCapability("pickAddress")) {
  const address = await onegate.pickAddress("Choose a payout address");
  console.log(address);
}
```

## Wallet authentication

Do not call `authenticate` on page load. Use it only when the player chooses an
action that needs website authentication.

```ts
const response = await onegate.authenticate({
  domain: location.host,
  nonce: crypto.randomUUID(),
  issuedAt: new Date().toISOString()
});
```

## Optional native capabilities

The SDK treats these capabilities as optional because they may land in different
OneGate app versions:

- `pickAddress`
- `scanQRCode`
- `pickAsset`
- `share`

Use `hasCapability` before showing UI that depends on one of them.

```ts
if (onegate.hasCapability("share")) {
  await onegate.share({
    title: "My OneGate run",
    text: "I just cleared this stage.",
    uri: location.href
  });
}
```

## Game runtime events

Game runtime lifecycle events can be handled through the client. The returned
function removes the listener.

```ts
const dispose = onegate.onRuntimeEvent("runtimepause", () => {
  pauseGameLoop();
});

dispose();
```

## Orientation

Games should still be responsive and mobile-first. Orientation locking is only a
runtime hint and can fail on some platforms.

```ts
import { lockOrientation } from "@onegate/game-sdk";

const orientationLocked = await lockOrientation("landscape");
if (!orientationLocked) {
  // Keep the responsive layout active when the host cannot lock orientation.
}
```

## Development principles

- Feature-detect optional OneGate capabilities.
- Keep wallet requests tied to user intent.
- Keep loading, progress, and error states inside the game.
- Do not depend on OneGate adapting a third-party game's DOM or CSS.
- Do not store private keys, seed phrases, or signing payloads in game storage.
