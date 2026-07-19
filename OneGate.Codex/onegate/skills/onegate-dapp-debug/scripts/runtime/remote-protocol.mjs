import {
  createCipheriv,
  createDecipheriv,
  createHash,
  createPrivateKey,
  createPublicKey,
  diffieHellman,
  generateKeyPairSync,
  hkdfSync,
  randomBytes,
  sign,
  verify,
} from "node:crypto";
import { chmod, mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";

export const REMOTE_PROTOCOL_VERSION = 1;
export const REMOTE_PROTOCOL_INFO = Buffer.from("OneGate.RemoteDebug.v1", "utf8");
const MAX_FRAME_LENGTH = 16 * 1024 * 1024;

export function base64UrlEncode(value) {
  return Buffer.from(value).toString("base64url");
}

export function base64UrlDecode(value) {
  if (typeof value !== "string" || value.length === 0 || !/^[A-Za-z0-9_-]+$/u.test(value)) {
    throw protocolError("INVALID_BASE64URL", "The value is not valid base64url.");
  }
  return Buffer.from(value, "base64url");
}

export function sha256(...values) {
  const hash = createHash("sha256");
  for (const value of values) hash.update(value);
  return hash.digest();
}

export function keyId(publicKey) {
  return sha256(publicKey).toString("hex").slice(0, 16);
}

export function protocolError(code, message, options) {
  const error = new Error(message, options);
  error.code = code;
  return error;
}

export function createDebugIdentity() {
  const { privateKey, publicKey } = generateKeyPairSync("ec", {
    namedCurve: "prime256v1",
  });
  return identityFromPrivateKey(privateKey);
}

export function identityFromPrivateKey(value) {
  const privateKey = value?.type === "private" ? value : createPrivateKey(value);
  const publicKey = createPublicKey(privateKey);
  const publicDer = publicKey.export({ type: "spki", format: "der" });
  const publicJwk = publicKey.export({ format: "jwk" });
  const publicRaw = Buffer.concat([
    Buffer.from([0x04]),
    Buffer.from(publicJwk.x, "base64url"),
    Buffer.from(publicJwk.y, "base64url"),
  ]);
  return Object.freeze({
    id: keyId(publicDer),
    privateKey,
    publicKey,
    publicDer,
    publicRaw,
    privateDer: privateKey.export({ type: "pkcs8", format: "der" }),
    sign(payload) {
      return sign("sha256", payload, { key: privateKey, dsaEncoding: "ieee-p1363" });
    },
  });
}

export function verifyIdentitySignature(publicDer, payload, signature) {
  try {
    const publicKey = createPublicKey({ key: publicDer, type: "spki", format: "der" });
    return verify("sha256", payload, {
      key: publicKey,
      dsaEncoding: "ieee-p1363",
    }, signature);
  } catch {
    return false;
  }
}

export function createEphemeralKey() {
  const { privateKey, publicKey } = generateKeyPairSync("ec", {
    namedCurve: "prime256v1",
  });
  return Object.freeze({
    privateKey,
    publicDer: publicKey.export({ type: "spki", format: "der" }),
    derive(peerPublicDer) {
      const publicKey = createPublicKey({ key: peerPublicDer, type: "spki", format: "der" });
      return diffieHellman({ privateKey, publicKey });
    },
  });
}

export class RemoteDebuggerIdentityStore {
  constructor({ directory, fileName = "remote-debugger-identity.json" }) {
    this.directory = directory;
    this.filePath = path.join(directory, fileName);
  }

  async load() {
    try {
      const document = JSON.parse(await readFile(this.filePath, "utf8"));
      if (document.schemaVersion !== 1 || typeof document.privateKey !== "string") {
        throw new Error("Unsupported remote-debugger identity document.");
      }
      return identityFromPrivateKey({
        key: base64UrlDecode(document.privateKey),
        type: "pkcs8",
        format: "der",
      });
    } catch (error) {
      if (error?.code !== "ENOENT") throw error;
      return this.create();
    }
  }

  async create() {
    const identity = createDebugIdentity();
    await mkdir(this.directory, { recursive: true });
    const temporaryPath = `${this.filePath}.${process.pid}.${Date.now()}.tmp`;
    await writeFile(temporaryPath, `${JSON.stringify({
      schemaVersion: 1,
      privateKey: base64UrlEncode(identity.privateDer),
    })}\n`, { mode: 0o600 });
    await chmod(temporaryPath, 0o600).catch(() => undefined);
    await rename(temporaryPath, this.filePath);
    await chmod(this.filePath, 0o600).catch(() => undefined);
    return identity;
  }
}

export class SecureChannel {
  constructor(role, sharedSecret, pairingSecret, transcriptHash) {
    if (!Buffer.isBuffer(pairingSecret) || pairingSecret.length !== 32) {
      throw protocolError("INVALID_SECRET", "The pairing or reconnect secret must be 32 bytes.");
    }
    const salt = sha256(pairingSecret, transcriptHash);
    const material = Buffer.from(hkdfSync(
      "sha256",
      sharedSecret,
      salt,
      REMOTE_PROTOCOL_INFO,
      72,
    ));
    const debuggerKey = Buffer.from(material.subarray(0, 32));
    const debugTargetKey = Buffer.from(material.subarray(32, 64));
    const debuggerNonce = Buffer.from(material.subarray(64, 68));
    const debugTargetNonce = Buffer.from(material.subarray(68, 72));
    material.fill(0);
    if (role === "remote-debugger") {
      this.sendKey = debuggerKey;
      this.receiveKey = debugTargetKey;
      this.sendNoncePrefix = debuggerNonce;
      this.receiveNoncePrefix = debugTargetNonce;
    } else if (role === "debug-target") {
      this.sendKey = debugTargetKey;
      this.receiveKey = debuggerKey;
      this.sendNoncePrefix = debugTargetNonce;
      this.receiveNoncePrefix = debuggerNonce;
    } else {
      throw protocolError("INVALID_ROLE", `Unknown secure-channel role: ${role}`);
    }
    this.nextSendSequence = 1n;
    this.nextReceiveSequence = 1n;
  }

  encrypt(value) {
    const plaintext = Buffer.isBuffer(value) ? value : Buffer.from(value);
    const sequenceBytes = Buffer.alloc(8);
    sequenceBytes.writeBigUInt64BE(this.nextSendSequence);
    const nonce = Buffer.concat([this.sendNoncePrefix, sequenceBytes]);
    const cipher = createCipheriv("aes-256-gcm", this.sendKey, nonce, { authTagLength: 16 });
    cipher.setAAD(sequenceBytes);
    const encrypted = Buffer.concat([cipher.update(plaintext), cipher.final()]);
    const result = Buffer.concat([sequenceBytes, encrypted, cipher.getAuthTag()]);
    this.nextSendSequence += 1n;
    return result;
  }

  decrypt(value) {
    const frame = Buffer.from(value);
    if (frame.length < 24) throw protocolError("INVALID_FRAME", "Encrypted frame is too short.");
    const sequenceBytes = frame.subarray(0, 8);
    const sequence = sequenceBytes.readBigUInt64BE();
    if (sequence !== this.nextReceiveSequence) {
      throw protocolError("INVALID_SEQUENCE", "Encrypted frame sequence is invalid.");
    }
    const nonce = Buffer.concat([this.receiveNoncePrefix, sequenceBytes]);
    const decipher = createDecipheriv("aes-256-gcm", this.receiveKey, nonce, { authTagLength: 16 });
    decipher.setAAD(sequenceBytes);
    decipher.setAuthTag(frame.subarray(-16));
    const plaintext = Buffer.concat([decipher.update(frame.subarray(8, -16)), decipher.final()]);
    this.nextReceiveSequence += 1n;
    return plaintext;
  }

  dispose() {
    this.sendKey.fill(0);
    this.receiveKey.fill(0);
    this.sendNoncePrefix.fill(0);
    this.receiveNoncePrefix.fill(0);
  }
}

export class FramedSocket {
  constructor(socket) {
    this.socket = socket;
    this.buffer = Buffer.alloc(0);
    this.frames = [];
    this.waiters = [];
    this.closedError = undefined;
    socket.on("data", (chunk) => this.#accept(chunk));
    socket.once("end", () => this.#close(protocolError("CONNECTION_CLOSED", "The peer closed the connection.")));
    socket.once("close", () => this.#close(protocolError("CONNECTION_CLOSED", "The connection closed.")));
    socket.once("error", (error) => this.#close(error));
  }

  async read(timeoutMs = 15_000) {
    if (this.frames.length > 0) return this.frames.shift();
    if (this.closedError) throw this.closedError;
    return new Promise((resolve, reject) => {
      const waiter = { resolve, reject };
      this.waiters.push(waiter);
      waiter.timeout = setTimeout(() => {
        const index = this.waiters.indexOf(waiter);
        if (index !== -1) this.waiters.splice(index, 1);
        reject(protocolError("CONNECTION_TIMEOUT", "Timed out waiting for the remote peer."));
      }, timeoutMs);
    });
  }

  write(frame) {
    if (this.closedError || this.socket.destroyed) throw this.closedError
      ?? protocolError("CONNECTION_CLOSED", "The connection is closed.");
    const payload = Buffer.from(frame);
    if (payload.length > MAX_FRAME_LENGTH) throw protocolError("FRAME_TOO_LARGE", "Frame exceeds 16 MiB.");
    const header = Buffer.alloc(4);
    header.writeUInt32BE(payload.length);
    this.socket.write(Buffer.concat([header, payload]));
  }

  writeJson(value) {
    this.write(Buffer.from(JSON.stringify(value), "utf8"));
  }

  async readJson(timeoutMs) {
    try {
      return JSON.parse((await this.read(timeoutMs)).toString("utf8"));
    } catch (error) {
      if (error instanceof SyntaxError) {
        throw protocolError("INVALID_JSON", "The remote peer sent invalid JSON.", { cause: error });
      }
      throw error;
    }
  }

  destroy() {
    this.socket.destroy();
  }

  #accept(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (this.buffer.length >= 4) {
      const length = this.buffer.readUInt32BE(0);
      if (length > MAX_FRAME_LENGTH) {
        this.#close(protocolError("FRAME_TOO_LARGE", "Frame exceeds 16 MiB."));
        this.socket.destroy();
        return;
      }
      if (this.buffer.length < 4 + length) return;
      const frame = this.buffer.subarray(4, 4 + length);
      this.buffer = this.buffer.subarray(4 + length);
      const waiter = this.waiters.shift();
      if (waiter) {
        clearTimeout(waiter.timeout);
        waiter.resolve(frame);
      } else {
        this.frames.push(frame);
      }
    }
  }

  #close(error) {
    if (this.closedError) return;
    this.closedError = error;
    for (const waiter of this.waiters.splice(0)) {
      clearTimeout(waiter.timeout);
      waiter.reject(error);
    }
  }
}

export function createPairingInvitation({
  pairingId,
  expiresAt,
  debuggerName,
  debuggerPublicKey,
  pairingSecret = randomBytes(32),
  endpoints,
}) {
  const query = new URLSearchParams({
    v: String(REMOTE_PROTOCOL_VERSION),
    id: pairingId,
    expires: String(Math.floor(expiresAt.getTime() / 1000)),
    debuggerKey: base64UrlEncode(debuggerPublicKey),
    secret: base64UrlEncode(pairingSecret),
  });
  if (debuggerName) query.set("debuggerName", debuggerName);
  for (const endpoint of endpoints) query.append("endpoint", endpoint);
  return `onegate-debug://pair?${query}`;
}

export function parsePairingInvitation(value, now = new Date()) {
  const url = new URL(value);
  if (url.protocol !== "onegate-debug:" || url.hostname !== "pair") {
    throw protocolError("INVALID_INVITATION", "Invalid OneGate debug pairing URI.");
  }
  const version = Number.parseInt(url.searchParams.get("v") ?? "", 10);
  if (version !== REMOTE_PROTOCOL_VERSION) {
    throw protocolError("UNSUPPORTED_PROTOCOL", `Unsupported pairing protocol version: ${version}.`);
  }
  const expiresAt = new Date(Number.parseInt(url.searchParams.get("expires") ?? "", 10) * 1000);
  if (!Number.isFinite(expiresAt.getTime()) || expiresAt <= now) {
    throw protocolError("INVITATION_EXPIRED", "The pairing invitation has expired.");
  }
  const pairingSecret = base64UrlDecode(url.searchParams.get("secret") ?? "");
  if (pairingSecret.length !== 32) {
    throw protocolError("INVALID_INVITATION", "The pairing secret must be 32 bytes.");
  }
  const endpoints = url.searchParams.getAll("endpoint");
  if (endpoints.length === 0 || endpoints.some((endpoint) => new URL(endpoint).protocol !== "tcp:")) {
    throw protocolError("INVALID_INVITATION", "The pairing invitation has no TCP endpoint.");
  }
  return {
    version,
    pairingId: url.searchParams.get("id"),
    expiresAt,
    debuggerName: url.searchParams.get("debuggerName") || undefined,
    debuggerPublicKey: base64UrlDecode(url.searchParams.get("debuggerKey") ?? ""),
    pairingSecret,
    endpoints,
  };
}
