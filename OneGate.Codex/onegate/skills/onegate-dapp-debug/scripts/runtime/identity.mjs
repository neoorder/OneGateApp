import {
  ECDH,
  createECDH,
  createHash,
  createPrivateKey,
  createPublicKey,
  randomBytes,
  sign as cryptoSign,
  verify as cryptoVerify,
} from "node:crypto";
import { chmod, mkdir, readFile, rename, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

export const ONEGATE_NETWORK = 860833102;
export const ONEGATE_ADDRESS_VERSION = 53;
export const ONEGATE_RUNTIME_VERSION = "1.0.1";

const IDENTITY_FILE = "identity.json";
const IDENTITY_SCHEMA_VERSION = 1;
const APP_DIRECTORY = path.join("NEO GLOBAL RESOURCES", "OneGate Codex Plugin");
const BASE58_ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

function sha256(value) {
  return createHash("sha256").update(value).digest();
}

function hash160(value) {
  return createHash("ripemd160").update(sha256(value)).digest();
}

function base64Url(value) {
  return Buffer.from(value).toString("base64url");
}

function encodeBase58(value) {
  const bytes = Buffer.from(value);
  let number = BigInt(`0x${bytes.toString("hex") || "0"}`);
  let result = "";
  while (number > 0n) {
    const remainder = Number(number % 58n);
    result = BASE58_ALPHABET[remainder] + result;
    number /= 58n;
  }
  for (const byte of bytes) {
    if (byte !== 0) break;
    result = `1${result}`;
  }
  return result || "1";
}

function base58Check(payload) {
  return encodeBase58(Buffer.concat([payload, sha256(sha256(payload)).subarray(0, 4)]));
}

function publicKeyObject(uncompressedPublicKey) {
  return createPublicKey({
    format: "jwk",
    key: {
      kty: "EC",
      crv: "P-256",
      x: base64Url(uncompressedPublicKey.subarray(1, 33)),
      y: base64Url(uncompressedPublicKey.subarray(33, 65)),
    },
  });
}

function createKeyMaterial(privateKey) {
  if (!Buffer.isBuffer(privateKey) || privateKey.length !== 32) {
    throw new Error("OneGate development private key must contain exactly 32 bytes.");
  }
  const ecdh = createECDH("prime256v1");
  try {
    ecdh.setPrivateKey(privateKey);
  } catch (error) {
    throw new Error("OneGate development private key is not a valid P-256 scalar.", {
      cause: error,
    });
  }
  const uncompressedPublicKey = ecdh.getPublicKey(undefined, "uncompressed");
  const compressedPublicKey = ecdh.getPublicKey(undefined, "compressed");
  const privateKeyObject = createPrivateKey({
    format: "jwk",
    key: {
      kty: "EC",
      crv: "P-256",
      x: base64Url(uncompressedPublicKey.subarray(1, 33)),
      y: base64Url(uncompressedPublicKey.subarray(33, 65)),
      d: base64Url(privateKey),
    },
  });
  return { compressedPublicKey, privateKeyObject, uncompressedPublicKey };
}

function verificationScript(compressedPublicKey) {
  return Buffer.concat([
    Buffer.from([0x0c, 0x21]),
    compressedPublicKey,
    Buffer.from([0x41, 0x56, 0xe7, 0xb3, 0x27]),
  ]);
}

function displayUInt160(scriptHash) {
  return `0x${Buffer.from(scriptHash).reverse().toString("hex")}`;
}

export function defaultIdentityDirectory() {
  if (process.env.ONEGATE_PLUGIN_STATE_DIR) {
    return path.resolve(process.env.ONEGATE_PLUGIN_STATE_DIR);
  }
  if (process.platform === "win32") {
    return path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), APP_DIRECTORY);
  }
  if (process.platform === "darwin") {
    return path.join(os.homedir(), "Library", "Application Support", APP_DIRECTORY);
  }
  return path.join(process.env.XDG_DATA_HOME || path.join(os.homedir(), ".local", "share"), APP_DIRECTORY);
}

export class DevelopmentIdentity {
  #privateKeyObject;
  #scriptHash;

  constructor(privateKey, createdAt = new Date().toISOString()) {
    const keyMaterial = createKeyMaterial(Buffer.from(privateKey));
    const script = verificationScript(keyMaterial.compressedPublicKey);
    this.#privateKeyObject = keyMaterial.privateKeyObject;
    this.#scriptHash = hash160(script);
    this.createdAt = createdAt;
    this.pubkey = keyMaterial.compressedPublicKey.toString("hex");
    this.hash = displayUInt160(this.#scriptHash);
    this.address = base58Check(Buffer.concat([
      Buffer.from([ONEGATE_ADDRESS_VERSION]),
      this.#scriptHash,
    ]));
    this.verificationScript = script.toString("base64");
    this.account = Object.freeze({
      hash: this.hash,
      address: this.address,
      label: "OneGate Codex Development Account",
      contract: {
        script: this.verificationScript,
        parameters: [{ type: "Signature" }],
        deployed: false,
      },
      extra: {
        mock: true,
        generated: true,
        createdAt: this.createdAt,
      },
    });
    Object.freeze(this);
  }

  sign(payload) {
    return cryptoSign("sha256", Buffer.from(payload), {
      key: this.#privateKeyObject,
      dsaEncoding: "ieee-p1363",
    });
  }

  scriptHashBytes() {
    return Buffer.from(this.#scriptHash);
  }

  publicSummary(statePath) {
    return {
      address: this.address,
      hash: this.hash,
      pubkey: this.pubkey,
      contract: this.account.contract,
      createdAt: this.createdAt,
      network: ONEGATE_NETWORK,
      addressVersion: ONEGATE_ADDRESS_VERSION,
      ...(statePath ? { statePath } : {}),
    };
  }
}

export function createDevelopmentIdentity(privateKey, createdAt) {
  return new DevelopmentIdentity(Buffer.from(privateKey), createdAt);
}

export function verifyP256Signature(payload, signature, compressedPublicKey) {
  const uncompressed = ECDH.convertKey(
    compressedPublicKey,
    "prime256v1",
    undefined,
    undefined,
    "uncompressed",
  );
  return cryptoVerify("sha256", Buffer.from(payload), {
    key: publicKeyObject(uncompressed),
    dsaEncoding: "ieee-p1363",
  }, Buffer.from(signature));
}

function generatePrivateKey() {
  for (;;) {
    const candidate = randomBytes(32);
    try {
      createKeyMaterial(candidate);
      return candidate;
    } catch {
      // A random invalid scalar is extraordinarily unlikely; retry without persisting it.
    }
  }
}

export class IdentityStore {
  #identity;
  #privateKey;

  constructor(options = {}) {
    this.directory = path.resolve(options.directory ?? defaultIdentityDirectory());
    this.filePath = path.join(this.directory, IDENTITY_FILE);
  }

  async load() {
    if (this.#identity) return this.#identity;
    let record;
    try {
      record = JSON.parse(await readFile(this.filePath, "utf8"));
    } catch (error) {
      if (error?.code !== "ENOENT") {
        throw new Error(`Unable to load the OneGate development identity at ${this.filePath}.`, {
          cause: error,
        });
      }
      return this.#createAndPersist();
    }
    if (
      record?.schemaVersion !== IDENTITY_SCHEMA_VERSION ||
      record?.network !== ONEGATE_NETWORK ||
      record?.addressVersion !== ONEGATE_ADDRESS_VERSION ||
      typeof record?.createdAt !== "string" ||
      typeof record?.privateKey !== "string"
    ) {
      throw new Error(`The OneGate development identity is invalid: ${this.filePath}`);
    }
    const privateKey = Buffer.from(record.privateKey, "base64");
    if (privateKey.length !== 32 || privateKey.toString("base64") !== record.privateKey) {
      throw new Error(`The OneGate development identity private key is invalid: ${this.filePath}`);
    }
    this.#privateKey = privateKey;
    this.#identity = new DevelopmentIdentity(privateKey, record.createdAt);
    return this.#identity;
  }

  async regenerate() {
    this.#identity = undefined;
    this.#privateKey = undefined;
    return this.#createAndPersist();
  }

  async #createAndPersist() {
    const privateKey = generatePrivateKey();
    const createdAt = new Date().toISOString();
    const record = {
      schemaVersion: IDENTITY_SCHEMA_VERSION,
      network: ONEGATE_NETWORK,
      addressVersion: ONEGATE_ADDRESS_VERSION,
      createdAt,
      privateKey: privateKey.toString("base64"),
    };
    await mkdir(this.directory, { recursive: true, mode: 0o700 });
    const temporaryPath = `${this.filePath}.${process.pid}.${randomBytes(6).toString("hex")}.tmp`;
    await writeFile(temporaryPath, `${JSON.stringify(record, null, 2)}\n`, {
      encoding: "utf8",
      mode: 0o600,
    });
    await rename(temporaryPath, this.filePath);
    await chmod(this.filePath, 0o600).catch(() => undefined);
    this.#privateKey = privateKey;
    this.#identity = new DevelopmentIdentity(privateKey, createdAt);
    return this.#identity;
  }

  async publicSummary() {
    return (await this.load()).publicSummary(this.filePath);
  }
}

export const defaultIdentityStore = new IdentityStore();
