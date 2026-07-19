import { randomUUID } from "node:crypto";
import { spawn, spawnSync } from "node:child_process";
import { constants as fsConstants } from "node:fs";
import {
  access,
  mkdtemp,
  readFile,
  rm,
  stat,
} from "node:fs/promises";
import os from "node:os";
import path from "node:path";

import { DapiEngine, DapiErrorCode } from "./dapi-engine.mjs";
import { defaultIdentityStore } from "./identity.mjs";

const DEFAULT_PROFILE_URL = new URL("../../assets/profiles/default.json", import.meta.url);
const MOCK_SOURCE_URL = new URL("../../assets/nep21-mock.js", import.meta.url);
const START_TIMEOUT_MS = 15_000;
const CDP_TIMEOUT_MS = 15_000;
const MAX_LOG_ENTRIES = 2_000;
const DAPI_BRIDGE_BINDING = "__OneGateMockBridge";
const DAPI_BRIDGE_RECEIVER = "__OneGateMockBridgeReceive";

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function removeSessionDirectory(sessionRoot) {
  return rm(sessionRoot, {
    recursive: true,
    force: true,
    maxRetries: 8,
    retryDelay: 100,
  });
}

function requireHttpUrl(value) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error("url must be a non-empty HTTP(S) URL.");
  }

  let parsed;
  try {
    parsed = new URL(value.trim());
  } catch {
    throw new Error("url must be a valid HTTP(S) URL.");
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    throw new Error("url must use http:// or https://.");
  }
  if (parsed.username || parsed.password) {
    throw new Error("url must not contain embedded credentials.");
  }
  return parsed.href;
}

async function isExecutableFile(candidate) {
  try {
    const info = await stat(candidate);
    if (!info.isFile()) {
      return false;
    }
    if (process.platform !== "win32") {
      await access(candidate, fsConstants.X_OK);
    }
    return true;
  } catch {
    return false;
  }
}

function resolveCommand(command) {
  const resolver = process.platform === "win32" ? "where.exe" : "which";
  const result = spawnSync(resolver, [command], {
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.status !== 0) {
    return undefined;
  }
  return result.stdout
    .split(/\r?\n/u)
    .map((line) => line.trim())
    .find(Boolean);
}

function platformBrowserCandidates() {
  if (process.platform === "win32") {
    const localAppData = process.env.LOCALAPPDATA;
    return [
      ["Chromium browser", resolveCommand("chromium")],
      ["Google Chrome", resolveCommand("chrome")],
      ["Microsoft Edge", resolveCommand("msedge")],
      ["Microsoft Edge", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"],
      ["Microsoft Edge", "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"],
      ...(localAppData
        ? [["Microsoft Edge", path.join(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")]]
        : []),
      ["Google Chrome", "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"],
      ["Google Chrome", "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe"],
      ...(localAppData
        ? [["Google Chrome", path.join(localAppData, "Google", "Chrome", "Application", "chrome.exe")]]
        : []),
    ];
  }

  if (process.platform === "darwin") {
    const home = os.homedir();
    return [
      ["Chromium browser", resolveCommand("chromium")],
      ["Google Chrome", resolveCommand("google-chrome")],
      ["Microsoft Edge", resolveCommand("microsoft-edge")],
      ["Microsoft Edge", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"],
      ["Microsoft Edge", path.join(home, "Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge")],
      ["Google Chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"],
      ["Google Chrome", path.join(home, "Applications/Google Chrome.app/Contents/MacOS/Google Chrome")],
      ["Chromium", "/Applications/Chromium.app/Contents/MacOS/Chromium"],
    ];
  }

  return [
    ["Microsoft Edge", resolveCommand("microsoft-edge")],
    ["Microsoft Edge", resolveCommand("microsoft-edge-stable")],
    ["Google Chrome", resolveCommand("google-chrome")],
    ["Google Chrome", resolveCommand("google-chrome-stable")],
    ["Chromium", resolveCommand("chromium")],
    ["Chromium", resolveCommand("chromium-browser")],
  ];
}

export async function discoverBrowsers() {
  const configured = [
    ["ONEGATE_BROWSER_EXECUTABLE", process.env.ONEGATE_BROWSER_EXECUTABLE],
    ["CHROMIUM_PATH", process.env.CHROMIUM_PATH],
    ["CHROME_PATH", process.env.CHROME_PATH],
  ].filter(([, executable]) => executable);
  const raw = [
    ...configured.map(([source, executable]) => ["Configured Chromium browser", path.resolve(executable), source]),
    ...platformBrowserCandidates(),
  ];
  const found = [];
  const seen = new Set();

  for (const [name, executable, configuredSource] of raw) {
    if (!executable) {
      continue;
    }
    const normalized = path.resolve(executable);
    const key = process.platform === "win32" ? normalized.toLowerCase() : normalized;
    if (seen.has(key) || !(await isExecutableFile(normalized))) {
      continue;
    }
    seen.add(key);
    found.push({
      name,
      executable: normalized,
      source: configuredSource ?? "auto",
    });
  }

  return found;
}

async function resolveBrowserExecutable(requested) {
  if (requested !== undefined) {
    if (typeof requested !== "string" || requested.trim().length === 0) {
      throw new Error("browserExecutable must be a non-empty absolute path.");
    }
    if (!path.isAbsolute(requested)) {
      throw new Error("browserExecutable must be an absolute path.");
    }
    const normalized = path.resolve(requested);
    if (!(await isExecutableFile(normalized))) {
      throw new Error(`Chromium browser executable was not found: ${normalized}`);
    }
    return normalized;
  }

  const browsers = await discoverBrowsers();
  if (browsers.length === 0) {
    throw new Error(
      "No CDP-compatible Chromium browser was found. Pass browserExecutable or set ONEGATE_BROWSER_EXECUTABLE, CHROMIUM_PATH, or CHROME_PATH.",
    );
  }
  return browsers[0].executable;
}

async function loadProfile(profilePath) {
  let source = DEFAULT_PROFILE_URL;
  if (profilePath !== undefined) {
    if (typeof profilePath !== "string" || profilePath.trim().length === 0) {
      throw new Error("profilePath must be a non-empty absolute path.");
    }
    if (!path.isAbsolute(profilePath)) {
      throw new Error("profilePath must be an absolute path.");
    }
    source = path.resolve(profilePath);
  }

  let profile;
  try {
    profile = JSON.parse(await readFile(source, "utf8"));
  } catch (error) {
    throw new Error(
      `Unable to read Browser Mock profile: ${error instanceof Error ? error.message : String(error)}`,
    );
  }
  if (profile === null || typeof profile !== "object" || Array.isArray(profile)) {
    throw new Error("Browser Mock profile must be a JSON object.");
  }
  return profile;
}

export function createInjectionSource(sessionId, profile, mockSource) {
  const envelopeJson = JSON.stringify({ sessionId, profile });
  const encodedEnvelope = JSON.stringify(envelopeJson)
    .replaceAll("\u2028", "\\u2028")
    .replaceAll("\u2029", "\\u2029");
  return [
    "if (globalThis.top === globalThis) {",
    "Object.defineProperty(globalThis, \"__ONEGATE_MOCK_CONFIG__\", {",
    "  configurable: false,",
    "  enumerable: false,",
    "  writable: false,",
    `  value: JSON.parse(${encodedEnvelope})`,
    "});",
    mockSource,
    "}",
    "//# sourceURL=onegate://browser-mock/nep21-mock.js",
  ].join("\n");
}

class CdpClient {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    this.waiters = new Map();
    this.eventHandler = undefined;
    this.closed = false;

    socket.addEventListener("message", (event) => {
      void this.handleMessage(event.data);
    });
    socket.addEventListener("close", () => this.handleClose());
    socket.addEventListener("error", () => this.handleClose());
  }

  static async connect(webSocketUrl) {
    const socket = new WebSocket(webSocketUrl);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(
        () => reject(new Error("Timed out while connecting to the Chromium DevTools endpoint.")),
        CDP_TIMEOUT_MS,
      );
      socket.addEventListener(
        "open",
        () => {
          clearTimeout(timeout);
          resolve();
        },
        { once: true },
      );
      socket.addEventListener(
        "error",
        () => {
          clearTimeout(timeout);
          reject(new Error("Unable to connect to the Chromium DevTools endpoint."));
        },
        { once: true },
      );
    });
    return new CdpClient(socket);
  }

  setEventHandler(handler) {
    this.eventHandler = handler;
  }

  async handleMessage(data) {
    let text;
    if (typeof data === "string") {
      text = data;
    } else if (data instanceof Blob) {
      text = await data.text();
    } else {
      text = Buffer.from(data).toString("utf8");
    }

    let message;
    try {
      message = JSON.parse(text);
    } catch {
      return;
    }

    if (message.id !== undefined) {
      const pending = this.pending.get(message.id);
      if (pending === undefined) {
        return;
      }
      this.pending.delete(message.id);
      clearTimeout(pending.timeout);
      if (message.error) {
        pending.reject(new Error(`${pending.method} failed: ${message.error.message}`));
      } else {
        pending.resolve(message.result ?? {});
      }
      return;
    }

    const methodWaiters = this.waiters.get(message.method);
    if (methodWaiters) {
      for (const waiter of [...methodWaiters]) {
        if (!waiter.predicate || waiter.predicate(message.params ?? {})) {
          methodWaiters.delete(waiter);
          clearTimeout(waiter.timeout);
          waiter.resolve(message.params ?? {});
        }
      }
    }
    this.eventHandler?.(message.method, message.params ?? {});
  }

  handleClose() {
    if (this.closed) {
      return;
    }
    this.closed = true;
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timeout);
      pending.reject(new Error("Chromium DevTools connection closed."));
    }
    this.pending.clear();
    for (const waiters of this.waiters.values()) {
      for (const waiter of waiters) {
        clearTimeout(waiter.timeout);
        waiter.reject(new Error("Chromium DevTools connection closed."));
      }
    }
    this.waiters.clear();
  }

  send(method, params = {}) {
    if (this.closed) {
      return Promise.reject(new Error("Chromium DevTools connection is closed."));
    }
    const id = this.nextId++;
    const response = new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`${method} timed out.`));
      }, CDP_TIMEOUT_MS);
      this.pending.set(id, { resolve, reject, timeout, method });
    });
    this.socket.send(JSON.stringify({ id, method, params }));
    return response;
  }

  waitForEvent(method, predicate, timeoutMs = CDP_TIMEOUT_MS) {
    if (this.closed) {
      return Promise.reject(new Error("Chromium DevTools connection is closed."));
    }
    return new Promise((resolve, reject) => {
      const waiters = this.waiters.get(method) ?? new Set();
      const waiter = {
        predicate,
        resolve,
        reject,
        timeout: setTimeout(() => {
          waiters.delete(waiter);
          reject(new Error(`${method} event timed out.`));
        }, timeoutMs),
      };
      waiters.add(waiter);
      this.waiters.set(method, waiters);
    });
  }

  close() {
    if (!this.closed) {
      this.socket.close();
    }
    this.handleClose();
  }
}

async function waitForDevToolsPort(userDataDir, childProcess, getStderr) {
  const portFile = path.join(userDataDir, "DevToolsActivePort");
  const deadline = Date.now() + START_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const [portLine] = (await readFile(portFile, "utf8")).split(/\r?\n/u);
      const port = Number.parseInt(portLine, 10);
      if (Number.isInteger(port) && port > 0 && port <= 65_535) {
        return port;
      }
    } catch {
      // The file appears only after Chromium has opened its DevTools endpoint.
    }
    if (childProcess.exitCode !== null && childProcess.exitCode !== 0) {
      throw new Error(
        `Chromium exited before DevTools was ready.${getStderr() ? ` ${getStderr()}` : ""}`,
      );
    }
    await delay(50);
  }
  throw new Error(
    `Timed out while waiting for Chromium DevTools.${getStderr() ? ` ${getStderr()}` : ""}`,
  );
}

async function findPageTarget(port) {
  const deadline = Date.now() + START_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(
        `http://127.0.0.1:${port}/json/new?${encodeURIComponent("about:blank")}`,
        {
          method: "PUT",
          signal: AbortSignal.timeout(2_000),
        },
      );
      if (response.ok) {
        const target = await response.json();
        if (target?.type === "page" && target.webSocketDebuggerUrl) {
          return target;
        }
      }
    } catch {
      // Chromium may report its port just before the HTTP endpoint is ready.
    }

    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/list`, {
        signal: AbortSignal.timeout(2_000),
      });
      if (response.ok) {
        const targets = await response.json();
        const target = targets.find((candidate) => candidate.type === "page");
        if (target?.webSocketDebuggerUrl) {
          return target;
        }
      }
    } catch {
      // Chromium may report its port just before the HTTP endpoint is ready.
    }
    await delay(50);
  }
  throw new Error("Chromium did not create a debuggable page target.");
}

function formatRemoteObject(remoteObject) {
  if (Object.prototype.hasOwnProperty.call(remoteObject, "value")) {
    if (typeof remoteObject.value === "string") {
      return remoteObject.value;
    }
    try {
      return JSON.stringify(remoteObject.value);
    } catch {
      return String(remoteObject.value);
    }
  }
  return remoteObject.unserializableValue ?? remoteObject.description ?? remoteObject.type;
}

function evaluationValue(response) {
  if (response.exceptionDetails) {
    const description =
      response.exceptionDetails.exception?.description ??
      response.exceptionDetails.text ??
      "JavaScript evaluation failed.";
    throw new Error(description);
  }
  const remoteObject = response.result ?? {};
  if (Object.prototype.hasOwnProperty.call(remoteObject, "value")) {
    return remoteObject.value;
  }
  if (remoteObject.unserializableValue !== undefined) {
    return remoteObject.unserializableValue;
  }
  return {
    type: remoteObject.type,
    subtype: remoteObject.subtype,
    description: remoteObject.description,
  };
}

function killProcessTree(childProcess) {
  if (!childProcess || childProcess.exitCode !== null) {
    return;
  }
  if (process.platform === "win32") {
    spawnSync("taskkill.exe", ["/PID", String(childProcess.pid), "/T", "/F"], {
      stdio: "ignore",
      windowsHide: true,
    });
    return;
  }
  childProcess.kill("SIGTERM");
}

export class BrowserMockSession {
  static async start(options) {
    const url = requireHttpUrl(options?.url);
    const browserExecutable = await resolveBrowserExecutable(options?.browserExecutable);
    const profile = await loadProfile(options?.profilePath);
    const identity = options?.identity ?? await defaultIdentityStore.load();
    const engine = new DapiEngine({ identity, profile });
    const sessionId = randomUUID();
    const sessionRoot = await mkdtemp(path.join(os.tmpdir(), "onegate-browser-mock-"));
    const userDataDir = path.join(sessionRoot, "profile");
    const headless = options?.headless === true;
    const preventEdgeCompatibilityRelaunch = process.platform === "win32"
      && path.basename(browserExecutable).toLowerCase() === "msedge.exe";
    const args = [
      ...(preventEdgeCompatibilityRelaunch ? ["--edge-skip-compat-layer-relaunch"] : []),
      "--remote-debugging-address=127.0.0.1",
      "--remote-debugging-port=0",
      `--user-data-dir=${userDataDir}`,
      "--no-first-run",
      "--no-default-browser-check",
      "--disable-default-apps",
      "--disable-extensions",
      "--window-size=1440,1000",
      ...(headless ? ["--headless=new", "--hide-scrollbars"] : []),
      "about:blank",
    ];

    let childProcess;
    let cdp;
    let session;
    let stderr = "";
    try {
      childProcess = spawn(browserExecutable, args, {
        stdio: ["ignore", "ignore", "pipe"],
        windowsHide: true,
      });
      childProcess.stderr.setEncoding("utf8");
      childProcess.stderr.on("data", (chunk) => {
        stderr = `${stderr}${chunk}`.slice(-16_384).trim();
      });

      const port = await waitForDevToolsPort(userDataDir, childProcess, () => stderr);
      const pageTarget = await findPageTarget(port);
      cdp = await CdpClient.connect(pageTarget.webSocketDebuggerUrl);
      session = new BrowserMockSession({
        sessionId,
        url,
        browserExecutable,
        headless,
        profile,
        identity,
        engine,
        sessionRoot,
        userDataDir,
        childProcess,
        cdp,
      });
      await session.initialize();
      return session;
    } catch (error) {
      const diagnostic = [
        session?.initializationStage
          ? `stage=${session.initializationStage}`
          : undefined,
        childProcess?.exitCode !== null && childProcess?.exitCode !== undefined
          ? `exitCode=${childProcess.exitCode}`
          : undefined,
        stderr ? `stderr=${stderr}` : undefined,
      ]
        .filter(Boolean)
        .join("; ");
      cdp?.close();
      killProcessTree(childProcess);
      await removeSessionDirectory(sessionRoot);
      throw new Error(
        `${error instanceof Error ? error.message : String(error)}${diagnostic ? ` (${diagnostic})` : ""}`,
        { cause: error },
      );
    }
  }

  constructor(options) {
    Object.assign(this, options);
    this.createdAt = new Date().toISOString();
    this.logs = [];
    this.nextLogSequence = 1;
    this.stopped = false;
    this.injectionIdentifier = undefined;
    this.cdp.setEventHandler((method, params) => this.handleEvent(method, params));
  }

  appendLog(entry) {
    this.logs.push({
      sequence: this.nextLogSequence++,
      timestamp: entry.timestamp ?? new Date().toISOString(),
      ...entry,
    });
    if (this.logs.length > MAX_LOG_ENTRIES) {
      this.logs.shift();
    }
  }

  handleEvent(method, params) {
    if (method === "Runtime.bindingCalled" && params.name === DAPI_BRIDGE_BINDING) {
      void this.handleBridgeRequest(params).catch((error) => {
        this.appendLog({
          source: "bridge",
          level: "error",
          message: error instanceof Error ? error.message : String(error),
        });
      });
      return;
    }
    if (method === "Runtime.consoleAPICalled") {
      const args = (params.args ?? []).map((argument) => ({
        type: argument.type,
        value: formatRemoteObject(argument),
      }));
      this.appendLog({
        source: "console",
        level: params.type ?? "log",
        message: args.map((argument) => argument.value).join(" "),
        args,
        timestamp: params.timestamp
          ? new Date(params.timestamp).toISOString()
          : undefined,
      });
      return;
    }
    if (method === "Runtime.exceptionThrown") {
      this.appendLog({
        source: "runtime",
        level: "error",
        message:
          params.exceptionDetails?.exception?.description ??
          params.exceptionDetails?.text ??
          "Uncaught exception",
      });
      return;
    }
    if (method === "Log.entryAdded") {
      this.appendLog({
        source: params.entry?.source ?? "browser",
        level: params.entry?.level ?? "info",
        message: params.entry?.text ?? "",
        url: params.entry?.url,
        lineNumber: params.entry?.lineNumber,
        timestamp: params.entry?.timestamp
          ? new Date(params.entry.timestamp).toISOString()
          : undefined,
      });
    }
  }

  async handleBridgeRequest(params) {
    let request;
    try {
      request = JSON.parse(params.payload);
    } catch {
      throw new Error("The DApp sent an invalid OneGate bridge payload.");
    }
    if (
      request?.sessionId !== this.sessionId ||
      typeof request?.id !== "string" ||
      typeof request?.method !== "string" ||
      !Array.isArray(request?.args)
    ) {
      throw new Error("The DApp sent an invalid OneGate bridge request.");
    }

    let response;
    try {
      const contextResponse = await this.cdp.send("Runtime.evaluate", {
        contextId: params.executionContextId,
        expression: "({ host: location.hostname, topLevel: globalThis.top === globalThis })",
        returnByValue: true,
      });
      const pageContext = evaluationValue(contextResponse);
      if (pageContext?.topLevel !== true) {
        throw Object.assign(new Error("OneGate Browser Mock is available only in the top-level DApp document."), {
          code: DapiErrorCode.INVALID,
        });
      }
      const result = await this.engine.invoke(request.method, request.args, {
        host: pageContext.host,
      });
      response = { id: request.id, ok: true, result };
    } catch (error) {
      response = {
        id: request.id,
        ok: false,
        error: {
          code: Number.isInteger(error?.code) ? error.code : DapiErrorCode.UNKNOWN,
          message: error instanceof Error ? error.message : String(error),
          ...(error?.data !== undefined ? { data: error.data } : {}),
        },
      };
    }

    const encodedResponse = JSON.stringify(JSON.stringify(response));
    const delivered = await this.cdp.send("Runtime.evaluate", {
      contextId: params.executionContextId,
      expression: `globalThis[${JSON.stringify(DAPI_BRIDGE_RECEIVER)}](${encodedResponse})`,
      returnByValue: true,
    });
    evaluationValue(delivered);
  }

  ensureRunning() {
    if (this.stopped || this.childProcess.exitCode !== null) {
      throw new Error(`Browser Mock session ${this.sessionId} is not running.`);
    }
  }

  async initialize() {
    this.initializationStage = "enable-domains";
    await Promise.all([
      this.cdp.send("Page.enable"),
      this.cdp.send("Runtime.enable"),
      this.cdp.send("Log.enable"),
    ]);
    this.initializationStage = "register-private-dapi-bridge";
    await this.cdp.send("Runtime.addBinding", { name: DAPI_BRIDGE_BINDING });
    const [mockSource] = await Promise.all([readFile(MOCK_SOURCE_URL, "utf8")]);
    const publicProfile = this.engine.publicConfiguration(this.sessionId).profile;
    const injection = createInjectionSource(this.sessionId, publicProfile, mockSource);
    this.initializationStage = "register-document-start-script";
    const registered = await this.cdp.send("Page.addScriptToEvaluateOnNewDocument", {
      source: injection,
    });
    this.injectionIdentifier = registered.identifier;

    this.initializationStage = "navigate";
    const navigation = await this.cdp.send("Page.navigate", { url: this.url });
    if (navigation.errorText) {
      throw new Error(`Navigation failed: ${navigation.errorText}`);
    }
    this.initializationStage = "wait-for-provider";
    await this.waitUntilReady();
    this.initializationStage = "ready";
  }

  async waitUntilReady() {
    const deadline = Date.now() + START_TIMEOUT_MS;
    let lastError;
    while (Date.now() < deadline) {
      try {
        const snapshot = await this.snapshot();
        if (
          snapshot.mock === true &&
          (snapshot.readyState === "interactive" || snapshot.readyState === "complete")
        ) {
          return snapshot;
        }
      } catch (error) {
        lastError = error;
      }
      await delay(50);
    }
    throw new Error(
      `OneGate provider was not ready after navigation.${lastError ? ` ${lastError.message}` : ""}`,
    );
  }

  async evaluate(expression) {
    this.ensureRunning();
    if (typeof expression !== "string" || expression.trim().length === 0) {
      throw new Error("expression must be a non-empty JavaScript expression.");
    }
    if (expression.length > 100_000) {
      throw new Error("expression must not exceed 100,000 characters.");
    }
    const response = await this.cdp.send("Runtime.evaluate", {
      expression,
      awaitPromise: true,
      returnByValue: true,
      userGesture: true,
    });
    return evaluationValue(response);
  }

  async snapshot() {
    return this.evaluate(`(() => {
      const provider = window.OneGateDapiProvider;
      return {
        href: location.href,
        origin: location.origin,
        title: document.title,
        readyState: document.readyState,
        mock: Boolean(window.__OneGateMock && provider && provider.extra && provider.extra.mock),
        provider: provider ? {
          name: provider.name,
          version: provider.version,
          dapiVersion: provider.dapiVersion,
          network: provider.network,
          supportedNetworks: provider.supportedNetworks,
          compatibility: provider.compatibility,
          extra: provider.extra
        } : null
      };
    })()`);
  }

  async status() {
    const snapshot = await this.snapshot();
    return {
      sessionId: this.sessionId,
      createdAt: this.createdAt,
      requestedUrl: this.url,
      browserExecutable: this.browserExecutable,
      headless: this.headless,
      injectionIdentifier: this.injectionIdentifier,
      transactionMode: this.engine.transactionMode,
      identity: this.identity.publicSummary(),
      ...snapshot,
    };
  }

  getLogs(afterSequence = 0) {
    this.ensureRunning();
    if (!Number.isInteger(afterSequence) || afterSequence < 0) {
      throw new Error("afterSequence must be a non-negative integer.");
    }
    return this.logs.filter((entry) => entry.sequence > afterSequence);
  }

  async getTrace() {
    return this.evaluate("window.__OneGateMock ? window.__OneGateMock.getTrace() : []");
  }

  async screenshot() {
    this.ensureRunning();
    const result = await this.cdp.send("Page.captureScreenshot", {
      format: "png",
      fromSurface: true,
      captureBeyondViewport: false,
    });
    return result.data;
  }

  async reload(ignoreCache = false) {
    this.ensureRunning();
    const contextCleared = this.cdp
      .waitForEvent("Runtime.executionContextsCleared", undefined, 2_000)
      .catch(() => undefined);
    await this.cdp.send("Page.reload", { ignoreCache: ignoreCache === true });
    await contextCleared;
    return this.waitUntilReady();
  }

  async stop() {
    if (this.stopped) {
      return;
    }
    this.stopped = true;
    try {
      await this.cdp.send("Browser.close");
    } catch {
      // Closing the browser usually closes the CDP socket before the reply arrives.
    }
    this.cdp.close();

    const deadline = Date.now() + 2_000;
    while (this.childProcess.exitCode === null && Date.now() < deadline) {
      await delay(50);
    }
    killProcessTree(this.childProcess);
    await removeSessionDirectory(this.sessionRoot);
  }
}
