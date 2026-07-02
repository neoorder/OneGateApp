export type OneGateJson = null | boolean | number | string | OneGateJson[] | { [key: string]: OneGateJson };

export interface OneGateAccount {
  address?: string;
  label?: string;
  publicKey?: string;
  scriptHash?: string;
  [key: string]: unknown;
}

export interface AuthenticationChallengePayload {
  [key: string]: unknown;
}

export interface AuthenticationResponsePayload {
  [key: string]: unknown;
}

export interface OneGateInvocation {
  hash: string;
  operation: string;
  args?: OneGateJson[];
  [key: string]: unknown;
}

export interface OneGateTransactionOptions {
  suggestedSystemFee?: number | string;
  extraSystemFee?: number | string;
  [key: string]: unknown;
}

export interface OneGateShareRequest {
  title?: string;
  text?: string;
  uri?: string;
}

export interface OneGateAsset {
  hash?: string;
  symbol?: string;
  decimals?: number;
  name?: string;
  [key: string]: unknown;
}

export interface OneGateSignedMessage {
  [key: string]: unknown;
}

export interface OneGateProvider {
  name: string;
  version: string;
  dapiVersion: string;
  compatibility: readonly string[];
  connected: boolean;
  network: number;
  supportedNetworks: readonly number[];
  icon: string;
  website: string;
  extra: Record<string, unknown>;

  on(event: string, listener: EventListener): void;
  removeListener(event: string, listener: EventListener): void;

  authenticate(payload: AuthenticationChallengePayload): Promise<AuthenticationResponsePayload>;
  getAccounts(): Promise<OneGateAccount[]>;
  getBalance(asset: string, account: string): Promise<string | number>;
  send(asset: string, from: string | null, to: string, amount: string | number, data?: OneGateJson): Promise<string>;
  call(invocation: OneGateInvocation): Promise<unknown>;
  invoke(
    invocations: OneGateInvocation[],
    signers?: unknown[],
    attributes?: unknown[],
    options?: OneGateTransactionOptions
  ): Promise<string>;
  makeTransaction(
    invocations: OneGateInvocation[],
    signers?: unknown[],
    attributes?: unknown[],
    options?: OneGateTransactionOptions
  ): Promise<unknown>;
  sign(context: unknown): Promise<unknown>;
  signMessage(message: string, account?: string | null, options?: Record<string, unknown>): Promise<OneGateSignedMessage>;
  relay(context: unknown): Promise<string>;
  getBlock(hashOrIndex: string | number): Promise<unknown>;
  getBlockCount(): Promise<number>;
  getTransaction(txid: string): Promise<unknown>;
  getApplicationLog(txid: string): Promise<unknown>;
  getStorage(hash: string, key: string): Promise<string>;
  getTokenInfo(hash: string): Promise<unknown>;

  pickAddress?(prompt?: string): Promise<string>;
  scanQRCode?(): Promise<string>;
  pickAsset?(): Promise<OneGateAsset>;
  share?(request: OneGateShareRequest): Promise<boolean>;
}

export interface OneGateGameRuntime {
  version: number;
  reload(): void;
  on(eventName: string, listener: EventListener): void;
  removeListener(eventName: string, listener: EventListener): void;
}

export interface GetProviderOptions {
  timeoutMs?: number;
  requestProviderEvent?: boolean;
}

export type OptionalOneGateCapability = "pickAddress" | "scanQRCode" | "pickAsset" | "share";

export type OneGateOrientationLockType =
  | "any"
  | "natural"
  | "landscape"
  | "portrait"
  | "portrait-primary"
  | "portrait-secondary"
  | "landscape-primary"
  | "landscape-secondary";

type LockableScreenOrientation = ScreenOrientation & {
  lock?: (orientation: OneGateOrientationLockType) => Promise<void>;
};

export class OneGateCapabilityError extends Error {
  constructor(readonly capability: OptionalOneGateCapability) {
    super(`OneGate capability is not available: ${capability}`);
    this.name = "OneGateCapabilityError";
  }
}

export class OneGateProviderTimeoutError extends Error {
  constructor(timeoutMs: number) {
    super(`OneGate provider was not available within ${timeoutMs} ms.`);
    this.name = "OneGateProviderTimeoutError";
  }
}

declare global {
  interface Window {
    OneGateDapiProvider?: OneGateProvider;
    OneGateGameRuntime?: OneGateGameRuntime;
    __OneGateSystemInvoke?: (method: string, params?: unknown[]) => Promise<unknown>;
  }
}

export async function getOneGateProvider(options: GetProviderOptions = {}): Promise<OneGateProvider> {
  if (typeof window === "undefined") {
    throw new Error("OneGate provider is only available in a browser or WebView runtime.");
  }

  const timeoutMs = options.timeoutMs ?? 5_000;
  if (window.OneGateDapiProvider) {
    return window.OneGateDapiProvider;
  }

  return new Promise<OneGateProvider>((resolve, reject) => {
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    const cleanup = () => {
      window.removeEventListener("Neo.DapiProvider.ready", onReady);
      if (timeoutId !== undefined) {
        clearTimeout(timeoutId);
      }
    };

    const resolveProvider = (provider: OneGateProvider) => {
      cleanup();
      resolve(provider);
    };

    const onReady = (event: Event) => {
      const detail = (event as CustomEvent<{ provider?: OneGateProvider }>).detail;
      const provider = detail?.provider ?? window.OneGateDapiProvider;
      if (provider) {
        resolveProvider(provider);
      }
    };

    window.addEventListener("Neo.DapiProvider.ready", onReady);
    timeoutId = setTimeout(() => {
      cleanup();
      reject(new OneGateProviderTimeoutError(timeoutMs));
    }, timeoutMs);

    if (options.requestProviderEvent !== false) {
      window.dispatchEvent(new CustomEvent("Neo.DapiProvider.request"));
    }

    if (window.OneGateDapiProvider) {
      resolveProvider(window.OneGateDapiProvider);
    }
  });
}

export class OneGateGameClient {
  constructor(readonly provider: OneGateProvider) {}

  hasCapability(capability: OptionalOneGateCapability): boolean {
    return typeof this.provider[capability] === "function";
  }

  requireCapability(capability: OptionalOneGateCapability): void {
    if (!this.hasCapability(capability)) {
      throw new OneGateCapabilityError(capability);
    }
  }

  getAccounts(): Promise<OneGateAccount[]> {
    return this.provider.getAccounts();
  }

  authenticate(payload: AuthenticationChallengePayload): Promise<AuthenticationResponsePayload> {
    return this.provider.authenticate(payload);
  }

  pickAddress(prompt?: string): Promise<string> {
    this.requireCapability("pickAddress");
    return this.provider.pickAddress!(prompt);
  }

  scanQRCode(): Promise<string> {
    this.requireCapability("scanQRCode");
    return this.provider.scanQRCode!();
  }

  pickAsset(): Promise<OneGateAsset> {
    this.requireCapability("pickAsset");
    return this.provider.pickAsset!();
  }

  share(request: OneGateShareRequest): Promise<boolean> {
    this.requireCapability("share");
    return this.provider.share!(request);
  }

  onRuntimeEvent(eventName: string, listener: EventListener): () => void {
    const runtime = getOneGateGameRuntime();
    if (runtime) {
      runtime.on(eventName, listener);
      return () => runtime.removeListener(eventName, listener);
    }

    const browserEventName = `OneGate.GameRuntime.${eventName}`;
    window.addEventListener(browserEventName, listener);
    return () => window.removeEventListener(browserEventName, listener);
  }
}

export async function createOneGateGameClient(options?: GetProviderOptions): Promise<OneGateGameClient> {
  return new OneGateGameClient(await getOneGateProvider(options));
}

export function getOneGateGameRuntime(): OneGateGameRuntime | undefined {
  if (typeof window === "undefined") {
    return undefined;
  }
  return window.OneGateGameRuntime;
}

export async function lockOrientation(orientation: OneGateOrientationLockType): Promise<boolean> {
  if (typeof window === "undefined") {
    throw new Error("Orientation locking requires a browser or WebView runtime.");
  }

  const screenOrientation = window.screen.orientation as LockableScreenOrientation | undefined;
  const nativeLock = screenOrientation?.lock;
  if (typeof nativeLock === "function") {
    await nativeLock.call(screenOrientation, orientation);
    return true;
  }

  if (window.__OneGateSystemInvoke) {
    await window.__OneGateSystemInvoke("screen.orientation.lock", [orientation]);
    return true;
  }

  return false;
}

export async function unlockOrientation(): Promise<boolean> {
  if (typeof window === "undefined") {
    return false;
  }

  const screenOrientation = window.screen.orientation as LockableScreenOrientation | undefined;
  const nativeUnlock = screenOrientation?.unlock;
  if (typeof nativeUnlock === "function") {
    nativeUnlock.call(screenOrientation);
    return true;
  }

  if (window.__OneGateSystemInvoke) {
    await window.__OneGateSystemInvoke("screen.orientation.unlock", []);
    return true;
  }

  return false;
}
