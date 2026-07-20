import { EventEmitter } from "node:events";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { randomBytes, randomUUID } from "node:crypto";

import { RemoteDebuggerAdvertiser } from "./remote-debugger-advertiser.mjs";
import {
  base64UrlDecode,
  base64UrlEncode,
  createEphemeralKey,
  createPairingInvitation,
  RemoteDebuggerIdentityStore,
  FramedSocket,
  keyId,
  protocolError,
  REMOTE_PROTOCOL_VERSION,
  SecureChannel,
  sha256,
  verifyIdentitySignature,
} from "./remote-protocol.mjs";

const CONNECTION_TIMEOUT_MS = 15_000;
const REQUEST_TIMEOUT_MS = 60_000;
const PAIRING_LIFETIME_MS = 2 * 60_000;

function requireString(value, name, maximumLength = 500) {
  if (typeof value !== "string" || value.trim().length === 0 || value.length > maximumLength) {
    throw protocolError("INVALID_ARGUMENT", `${name} must be a non-empty string of at most ${maximumLength} characters.`);
  }
  return value;
}

function publicAddresses() {
  const addresses = [];
  for (const entries of Object.values(os.networkInterfaces())) {
    for (const entry of entries ?? []) {
      if (entry.internal || entry.family !== "IPv4" || entry.address.startsWith("169.254.")) continue;
      addresses.push(entry.address);
    }
  }
  return [...new Set(addresses)]
    .sort((left, right) => addressPreference(left) - addressPreference(right))
    .slice(0, 4);
}

function addressPreference(address) {
  if (address.startsWith("192.168.")) return 0;
  if (address.startsWith("10.")) return 1;
  const second = Number.parseInt(address.split(".")[1], 10);
  if (address.startsWith("172.") && second >= 16 && second <= 31) return 2;
  return 3;
}

function endpointFor(address, port) {
  return `tcp://${address}:${port}`;
}

class DebugTargetStore {
  constructor(directory) {
    this.directory = directory;
    this.filePath = path.join(directory, "remote-debug-targets.json");
  }

  async load() {
    try {
      const document = JSON.parse(await readFile(this.filePath, "utf8"));
      if (document.schemaVersion !== 1 || !Array.isArray(document.debugTargets)) {
        throw new Error("Unsupported trusted debug-target document.");
      }
      return new Map(document.debugTargets.map((debugTarget) => [debugTarget.id, debugTarget]));
    } catch (error) {
      if (error?.code === "ENOENT") return new Map();
      throw error;
    }
  }

  async save(debugTargets) {
    await mkdir(this.directory, { recursive: true });
    const temporaryPath = `${this.filePath}.${process.pid}.${Date.now()}.tmp`;
    await writeFile(temporaryPath, `${JSON.stringify({
      schemaVersion: 1,
      debugTargets: [...debugTargets.values()],
    }, null, 2)}\n`, { mode: 0o600 });
    await rename(temporaryPath, this.filePath);
  }
}

class DebugTargetConnection extends EventEmitter {
  constructor({ record, socket, framed, channel }) {
    super();
    this.record = record;
    this.socket = socket;
    this.framed = framed;
    this.channel = channel;
    this.pending = new Map();
    this.nextRequestId = 1;
  }

  start() {
    this.readLoop = this.#readLoop();
    this.readLoop.catch((error) => this.#close(error));
  }

  async request(method, params = {}, timeoutMs = REQUEST_TIMEOUT_MS) {
    const id = `${process.pid}-${this.nextRequestId++}`;
    const promise = new Promise((resolve, reject) => {
      const entry = { resolve, reject };
      entry.timer = setTimeout(() => {
        this.pending.delete(id);
        reject(protocolError("DEBUG_TARGET_TIMEOUT", `The debug target did not answer ${method}.`));
      }, timeoutMs);
      this.pending.set(id, entry);
    });
    this.#send({ kind: "request", id, method, params });
    return promise;
  }

  sendEvent(method, params = {}) {
    this.#send({ kind: "event", method, params });
  }

  close() {
    this.framed.destroy();
  }

  #send(message) {
    this.framed.write(this.channel.encrypt(Buffer.from(JSON.stringify(message), "utf8")));
  }

  async #readLoop() {
    while (true) {
      const frame = await this.framed.read(24 * 60 * 60 * 1000);
      const message = JSON.parse(this.channel.decrypt(frame).toString("utf8"));
      if (message?.kind === "response") {
        const entry = this.pending.get(message.id);
        if (!entry) continue;
        this.pending.delete(message.id);
        clearTimeout(entry.timer);
        if (message.error) {
          entry.reject(protocolError(message.error.code ?? "DEBUG_TARGET_ERROR", message.error.message ?? "Debug-target request failed."));
        } else {
          entry.resolve(message.result);
        }
      } else if (message?.kind === "event" && typeof message.method === "string") {
        this.emit("event", message.method, message.params ?? {});
      } else {
        throw protocolError("INVALID_MESSAGE", "The debug target sent an invalid secure message.");
      }
    }
  }

  #close(error) {
    if (this.closed) return;
    this.closed = true;
    this.channel.dispose();
    for (const entry of this.pending.values()) {
      clearTimeout(entry.timer);
      entry.reject(error);
    }
    this.pending.clear();
    this.emit("close", error);
  }
}

export class DebugTargetGateway extends EventEmitter {
  constructor({ stateDirectory, debuggerName = os.hostname() }) {
    super();
    this.stateDirectory = stateDirectory;
    this.debuggerName = debuggerName;
    this.identityStore = new RemoteDebuggerIdentityStore({ directory: stateDirectory });
    this.debugTargetStore = new DebugTargetStore(stateDirectory);
    this.pairings = new Map();
    this.connections = new Map();
  }

  async start() {
    if (this.server) return this.summary();
    [this.identity, this.debugTargets] = await Promise.all([
      this.identityStore.load(),
      this.debugTargetStore.load(),
    ]);
    this.server = net.createServer((socket) => void this.#accept(socket));
    await new Promise((resolve, reject) => {
      this.server.once("error", reject);
      this.server.listen(0, "0.0.0.0", () => {
        this.server.off("error", reject);
        resolve();
      });
    });
    this.port = this.server.address().port;
    this.addresses = publicAddresses();
    this.advertiser = new RemoteDebuggerAdvertiser({
      debuggerId: this.identity.id,
      debuggerName: this.debuggerName,
      port: this.port,
      addresses: this.addresses,
    });
    await this.advertiser.start().catch((error) => this.emit("diagnostic", {
      level: "warning",
      code: "MDNS_UNAVAILABLE",
      message: error.message,
    }));
    return this.summary();
  }

  summary() {
    return {
      debuggerId: this.identity?.id,
      debuggerName: this.debuggerName,
      port: this.port,
      endpoints: this.port ? this.addresses.map((address) => endpointFor(address, this.port)) : [],
      trustedDebugTargetCount: this.debugTargets?.size ?? 0,
      connectedDebugTargetCount: this.connections.size,
    };
  }

  async createPairing({ debuggerName, lifetimeMs = PAIRING_LIFETIME_MS } = {}) {
    await this.start();
    const pairingId = randomUUID();
    const pairingSecret = randomBytes(32);
    const expiresAt = new Date(Date.now() + lifetimeMs);
    const name = debuggerName?.trim() || this.debuggerName;
    const endpoints = this.addresses.slice(0, 2).map((address) => endpointFor(address, this.port));
    if (endpoints.length === 0) {
      throw protocolError("NO_LAN_ENDPOINT", "No non-loopback IPv4 network endpoint is available for pairing.");
    }
    const invitation = createPairingInvitation({
      pairingId,
      expiresAt,
      debuggerName: name,
      debuggerPublicKey: this.identity.publicRaw,
      pairingSecret,
      endpoints,
    });
    this.pairings.set(pairingId, {
      pairingId,
      pairingSecret,
      expiresAt,
      debuggerName: name,
      invitation,
      status: "waiting",
    });
    return {
      pairingId,
      expiresAt: expiresAt.toISOString(),
      debuggerId: this.identity.id,
      debuggerName: name,
      endpoints,
      invitation,
      status: "waiting",
    };
  }

  pairingStatus(pairingId) {
    const pairing = this.pairings.get(requireString(pairingId, "pairingId"));
    if (!pairing) throw protocolError("PAIRING_NOT_FOUND", `Pairing was not found: ${pairingId}`);
    if (pairing.status === "waiting" && pairing.expiresAt <= new Date()) pairing.status = "expired";
    return {
      pairingId,
      status: pairing.status,
      expiresAt: pairing.expiresAt.toISOString(),
      debugTarget: pairing.debugTarget,
    };
  }

  cancelPairing(pairingId) {
    const pairing = this.pairings.get(requireString(pairingId, "pairingId"));
    if (!pairing) throw protocolError("PAIRING_NOT_FOUND", `Pairing was not found: ${pairingId}`);
    pairing.status = "cancelled";
    pairing.pairingSecret.fill(0);
    return { pairingId, status: "cancelled" };
  }

  listDebugTargets() {
    return {
      debugTargets: [...this.debugTargets.values()].map((debugTarget) => ({
        id: debugTarget.id,
        name: debugTarget.name,
        platform: debugTarget.platform,
        pairedAt: debugTarget.pairedAt,
        lastConnectedAt: debugTarget.lastConnectedAt,
        online: this.connections.has(debugTarget.id),
      })),
    };
  }

  async forgetDebugTarget(debugTargetId) {
    const id = requireString(debugTargetId, "debugTargetId");
    this.connections.get(id)?.close();
    if (!this.debugTargets.delete(id)) throw protocolError("DEBUG_TARGET_NOT_FOUND", `Debug target was not found: ${id}`);
    await this.debugTargetStore.save(this.debugTargets);
    return { debugTargetId: id, forgotten: true };
  }

  getConnection(debugTargetId) {
    const id = debugTargetId ?? (this.connections.size === 1 ? [...this.connections.keys()][0] : undefined);
    if (!id) {
      throw protocolError(
        this.connections.size === 0 ? "DEBUG_TARGET_OFFLINE" : "DEBUG_TARGET_REQUIRED",
        this.connections.size === 0
          ? "No trusted OneGate debug target is connected."
          : "More than one OneGate debug target is connected; specify a debug-target id.",
      );
    }
    const connection = this.connections.get(id);
    if (!connection) throw protocolError("DEBUG_TARGET_OFFLINE", `OneGate debug target is offline: ${id}`);
    return connection;
  }

  async request(debugTargetId, method, params) {
    return this.getConnection(debugTargetId).request(method, params);
  }

  async stop() {
    for (const connection of this.connections.values()) connection.close();
    this.connections.clear();
    for (const pairing of this.pairings.values()) pairing.pairingSecret.fill(0);
    this.pairings.clear();
    await this.advertiser?.stop().catch(() => undefined);
    if (this.server) await new Promise((resolve) => this.server.close(resolve));
    this.server = undefined;
  }

  async #accept(socket) {
    if ((this.handshakeCount ?? 0) >= 8) {
      socket.destroy();
      return;
    }
    this.handshakeCount = (this.handshakeCount ?? 0) + 1;
    socket.setNoDelay(true);
    socket.setKeepAlive(true, 10_000);
    const framed = new FramedSocket(socket);
    try {
      const hello = await framed.readJson(CONNECTION_TIMEOUT_MS);
      const connection = await this.#handshake(socket, framed, hello);
      const old = this.connections.get(connection.record.id);
      old?.close();
      this.connections.set(connection.record.id, connection);
      connection.on("event", (method, params) => this.emit("debugTargetEvent", connection.record.id, method, params));
      connection.once("close", (error) => {
        if (this.connections.get(connection.record.id) === connection) {
          this.connections.delete(connection.record.id);
          this.emit("debugTargetDisconnected", connection.record.id, error);
        }
      });
      connection.start();
      this.emit("debugTargetConnected", connection.record.id);
    } catch (error) {
      framed.destroy();
      this.emit("diagnostic", {
        level: "warning",
        code: error?.code ?? "HANDSHAKE_FAILED",
        message: error instanceof Error ? error.message : String(error),
      });
    } finally {
      this.handshakeCount -= 1;
    }
  }

  async #handshake(socket, framed, hello) {
    if (hello?.type !== "clientHello" || typeof hello.payload !== "string" || typeof hello.signature !== "string") {
      throw protocolError("UNSUPPORTED_PROTOCOL", "Invalid remote-debug client hello.");
    }
    const clientPayload = base64UrlDecode(hello.payload);
    const clientHello = JSON.parse(clientPayload.toString("utf8"));
    if (clientHello?.version !== REMOTE_PROTOCOL_VERSION) {
      throw protocolError("UNSUPPORTED_PROTOCOL", "Invalid remote-debug client hello.");
    }
    const signature = base64UrlDecode(hello.signature);
    const debugTargetPublicKey = base64UrlDecode(clientHello.identityKey);
    const debugTargetId = keyId(debugTargetPublicKey);
    if (clientHello.debugTargetId !== debugTargetId || !verifyIdentitySignature(debugTargetPublicKey, clientPayload, signature)) {
      throw protocolError("DEBUG_TARGET_AUTH_FAILED", "The debug-target identity signature is invalid.");
    }

    let secret;
    let pairing;
    let record;
    if (clientHello.mode === "pair") {
      pairing = this.pairings.get(clientHello.pairingId);
      if (!pairing || pairing.status !== "waiting" || pairing.expiresAt <= new Date()) {
        throw protocolError("PAIRING_EXPIRED", "The pairing request is not active.");
      }
      secret = pairing.pairingSecret;
    } else if (clientHello.mode === "trusted") {
      record = this.debugTargets.get(debugTargetId);
      if (!record || record.publicKey !== clientHello.identityKey) {
        throw protocolError("DEBUG_TARGET_NOT_TRUSTED", "The debug target is not trusted by this remote debugger.");
      }
      secret = base64UrlDecode(record.reconnectSecret);
    } else {
      throw protocolError("INVALID_HANDSHAKE", "Unknown remote-debug connection mode.");
    }

    const ephemeral = createEphemeralKey();
    const unsignedServer = {
      type: "serverHello",
      version: REMOTE_PROTOCOL_VERSION,
      debuggerId: this.identity.id,
      debuggerName: pairing?.debuggerName ?? this.debuggerName,
      identityKey: base64UrlEncode(this.identity.publicDer),
      ephemeralKey: base64UrlEncode(ephemeral.publicDer),
      nonce: base64UrlEncode(randomBytes(16)),
      clientHash: base64UrlEncode(sha256(clientPayload)),
    };
    const serverPayload = Buffer.from(JSON.stringify(unsignedServer), "utf8");
    const transcriptHash = sha256(clientPayload, serverPayload);
    framed.writeJson({
      type: "serverHello",
      payload: base64UrlEncode(serverPayload),
      signature: base64UrlEncode(this.identity.sign(transcriptHash)),
    });
    const channel = new SecureChannel(
      "remote-debugger",
      ephemeral.derive(base64UrlDecode(clientHello.ephemeralKey)),
      secret,
      transcriptHash,
    );
    if (clientHello.mode === "trusted") secret.fill(0);

    if (clientHello.mode === "pair") {
      const reconnectSecret = randomBytes(32);
      framed.write(channel.encrypt(Buffer.from(JSON.stringify({
        kind: "pair.complete",
        reconnectSecret: base64UrlEncode(reconnectSecret),
      }), "utf8")));
      const acknowledgement = JSON.parse(channel.decrypt(await framed.read(CONNECTION_TIMEOUT_MS)).toString("utf8"));
      if (acknowledgement?.kind !== "pair.ack") {
        throw protocolError("PAIRING_FAILED", "The debug target did not acknowledge persistent trust.");
      }
      record = {
        id: debugTargetId,
        name: requireString(clientHello.debugTargetName, "debugTargetName", 200),
        platform: typeof clientHello.platform === "string" ? clientHello.platform : "unknown",
        publicKey: clientHello.identityKey,
        reconnectSecret: base64UrlEncode(reconnectSecret),
        pairedAt: new Date().toISOString(),
        lastConnectedAt: new Date().toISOString(),
      };
      reconnectSecret.fill(0);
      this.debugTargets.set(record.id, record);
      await this.debugTargetStore.save(this.debugTargets);
      pairing.status = "paired";
      pairing.debugTarget = { id: record.id, name: record.name, platform: record.platform };
      pairing.pairingSecret.fill(0);
    } else {
      framed.write(channel.encrypt(Buffer.from(JSON.stringify({ kind: "ready" }), "utf8")));
      const acknowledgement = JSON.parse(channel.decrypt(await framed.read(CONNECTION_TIMEOUT_MS)).toString("utf8"));
      if (acknowledgement?.kind !== "ready.ack") {
        throw protocolError("HANDSHAKE_FAILED", "The trusted debug target did not acknowledge the remote debugger.");
      }
      record.lastConnectedAt = new Date().toISOString();
      await this.debugTargetStore.save(this.debugTargets);
    }
    socket.setTimeout(0);
    return new DebugTargetConnection({ record, socket, framed, channel });
  }
}
