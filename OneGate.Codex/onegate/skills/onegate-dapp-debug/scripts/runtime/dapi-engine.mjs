import { createHash, randomBytes } from "node:crypto";

import { ONEGATE_NETWORK, verifyP256Signature } from "./identity.mjs";

export const DapiErrorCode = Object.freeze({
  UNKNOWN: 10000,
  UNSUPPORTED: 10001,
  INVALID: 10002,
  NOTFOUND: 10003,
  FAILED: 10004,
  TIMEOUT: 10005,
  CANCELED: 10006,
  INSUFFICIENT_FUNDS: 10007,
  RPC_ERROR: 10008,
});

const FIXTURE_ONLY_READ_METHODS = new Set([
  "getBlock",
  "getTransaction",
  "getApplicationLog",
  "getStorage",
  "getTokenInfo",
]);

const NEP20_CLOCK_TOLERANCE_SECONDS = 300;

function sha256(value) {
  return createHash("sha256").update(value).digest();
}

function clone(value) {
  return value === undefined ? undefined : structuredClone(value);
}

function sameArguments(left, right) {
  try {
    return JSON.stringify(left) === JSON.stringify(right);
  } catch {
    return false;
  }
}

function findEntry(entries, method, args) {
  if (!Array.isArray(entries)) return undefined;
  return entries.find((entry) => entry && entry.method === method &&
    (!Object.hasOwn(entry, "args") || sameArguments(entry.args, args)));
}

function requireString(value, name) {
  if (typeof value !== "string" || value.length === 0) {
    throw new DapiEngineError(DapiErrorCode.INVALID, `${name} must be a non-empty string.`);
  }
  return value;
}

function requireObject(value, name) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new DapiEngineError(DapiErrorCode.INVALID, `${name} must be an object.`);
  }
  return value;
}

function uint32LE(value) {
  const result = Buffer.alloc(4);
  result.writeUInt32LE(value);
  return result;
}

function uint64LE(value) {
  const result = Buffer.alloc(8);
  result.writeBigUInt64LE(value);
  return result;
}

function requireUInt64(value, name) {
  let result;
  if (typeof value === "string" && /^\d+$/u.test(value)) {
    result = BigInt(value);
  } else if (typeof value === "number" && Number.isSafeInteger(value)) {
    result = BigInt(value);
  } else {
    throw new DapiEngineError(
      DapiErrorCode.INVALID,
      `${name} must be an unsigned 64-bit decimal string or safe integer.`,
    );
  }
  if (result < 0n || result > 0xffffffffffffffffn) {
    throw new DapiEngineError(DapiErrorCode.INVALID, `${name} must be an unsigned 64-bit integer.`);
  }
  return result;
}

function varUInt(value) {
  if (value < 0xfd) return Buffer.from([value]);
  if (value <= 0xffff) {
    const result = Buffer.alloc(3);
    result[0] = 0xfd;
    result.writeUInt16LE(value, 1);
    return result;
  }
  const result = Buffer.alloc(5);
  result[0] = 0xfe;
  result.writeUInt32LE(value, 1);
  return result;
}

function varString(value) {
  const bytes = Buffer.from(value, "utf8");
  return Buffer.concat([varUInt(bytes.length), bytes]);
}

function decodeBase64(value, name) {
  requireString(value, name);
  const bytes = Buffer.from(value, "base64");
  if (bytes.toString("base64") !== value) {
    throw new DapiEngineError(DapiErrorCode.INVALID, `${name} must be canonical base64.`);
  }
  return bytes;
}

function displayUInt256(hashBytes) {
  return `0x${Buffer.from(hashBytes).reverse().toString("hex")}`;
}

function defaultCallResult() {
  return {
    script: "",
    state: "HALT",
    gasconsumed: "0",
    notifications: [],
    stack: [],
  };
}

export class DapiEngineError extends Error {
  constructor(code, message, data) {
    super(message);
    this.name = "DapiEngineError";
    this.code = code;
    if (data !== undefined) this.data = clone(data);
  }
}

export class DapiEngine {
  constructor({ identity, profile }) {
    if (!identity) throw new Error("DapiEngine requires a development identity.");
    this.identity = identity;
    this.profile = clone(profile ?? {});
    this.transactionMode = this.profile.transactionMode ?? "offline";
    if (this.transactionMode !== "offline" && this.transactionMode !== "simulate") {
      throw new Error("Browser Mock transactionMode must be either offline or simulate.");
    }
  }

  publicConfiguration(sessionId) {
    return {
      sessionId,
      profile: {
        id: String(this.profile.id || "default"),
        transactionMode: this.transactionMode,
        provider: clone(this.profile.provider ?? {}),
      },
    };
  }

  async invoke(method, args, context = {}) {
    if (typeof method !== "string" || !Array.isArray(args)) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Invalid Browser Mock request envelope.");
    }
    const forcedError = findEntry(this.profile.errors, method, args);
    if (forcedError) {
      throw new DapiEngineError(
        Number.isInteger(forcedError.code) ? forcedError.code : DapiErrorCode.UNKNOWN,
        forcedError.message || "Mock dAPI request failed.",
        forcedError.data,
      );
    }
    const fixture = findEntry(this.profile.fixtures, method, args);
    if (fixture) return clone(fixture.result);

    switch (method) {
      case "authenticate": return this.#authenticate(args[0], context.host);
      case "getAccounts": return [clone(this.identity.account)];
      case "pickAddress": return this.identity.address;
      case "getBalance": return this.#getBalance(args[0], args[1]);
      case "signMessage": return this.#signMessage(args[0], args[1], args[2]);
      case "sign": return this.#signContext(args[0]);
      case "call":
        if (this.transactionMode === "offline") this.#rpcUnavailable("call");
        return defaultCallResult();
      case "getBlockCount":
        if (Number.isInteger(this.profile.blockCount)) return this.profile.blockCount;
        if (this.transactionMode === "simulate") return 0;
        this.#rpcUnavailable("getBlockCount");
        break;
      case "send": return this.#send(args);
      case "invoke": return this.#invokeTransaction("invoke", args);
      case "makeTransaction": return this.#makeTransaction(args);
      case "relay": return this.#relay(args[0]);
      default:
        if (FIXTURE_ONLY_READ_METHODS.has(method)) {
          throw new DapiEngineError(
            DapiErrorCode.NOTFOUND,
            `No Browser Mock fixture is configured for ${method}.`,
          );
        }
        throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, `Unknown dAPI method: ${method}.`);
    }
  }

  #getBalance(asset, account) {
    requireString(asset, "asset");
    requireString(account, "account");
    const aliases = new Set([this.identity.address, this.identity.hash]);
    if (!aliases.has(account)) {
      throw new DapiEngineError(DapiErrorCode.NOTFOUND, "Account not found.");
    }
    const balance = Array.isArray(this.profile.balances)
      ? this.profile.balances.find((entry) => entry && entry.asset === asset &&
        (entry.account === account || aliases.has(entry.account)))
      : undefined;
    return balance?.value ?? "0";
  }

  #signMessage(message, account, options) {
    requireString(message, "message");
    if (account !== undefined && account !== null && account !== this.identity.hash) {
      throw new DapiEngineError(DapiErrorCode.NOTFOUND, "Account not found.");
    }
    if (options !== undefined && options !== null) requireObject(options, "options");
    if (options?.isTypedData === true) {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "Typed data signing is not supported.");
    }
    if (options?.isLedgerCompatible === true) {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "Ledger compatible signing is not supported.");
    }
    let payload;
    try {
      payload = options?.isBase64Encoded === true ? Buffer.from(message, "base64") : Buffer.from(message, "utf8");
    } catch {
      throw new DapiEngineError(DapiErrorCode.INVALID, "message is not valid base64.");
    }
    if (options?.isBase64Encoded === true && payload.toString("base64") !== message) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "message is not canonical base64.");
    }
    return {
      payload: payload.toString("base64"),
      signature: this.identity.sign(payload).toString("base64"),
      account: this.identity.hash,
      pubkey: this.identity.pubkey,
    };
  }

  #authenticate(payload, host) {
    requireObject(payload, "payload");
    if (payload.action !== "Authentication") {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "Unsupported action.");
    }
    if (payload.grant_type !== "Signature") {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "Unsupported grant type.");
    }
    if (!Array.isArray(payload.allowed_algorithms) || !payload.allowed_algorithms.includes("ECDSA-P256")) {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "No supported algorithm.");
    }
    if (typeof payload.domain !== "string" || payload.domain.trim().length === 0) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Domain cannot be empty.");
    }
    if (typeof host !== "string" || payload.domain.toLowerCase() !== host.toLowerCase()) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Domain mismatch.");
    }
    if (!Array.isArray(payload.networks) || !payload.networks.includes(ONEGATE_NETWORK)) {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "No supported network.");
    }
    const nonce = requireUInt64(payload.nonce, "Nonce");
    if (!Number.isInteger(payload.timestamp) || payload.timestamp < 0 || payload.timestamp > 0xffffffff) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Timestamp must be an unsigned 32-bit integer.");
    }
    const now = Math.floor(Date.now() / 1000);
    if (Math.abs(now - payload.timestamp) > NEP20_CLOCK_TOLERANCE_SECONDS) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Request timestamp is outside the allowed clock tolerance.");
    }
    const timestamp = now;
    const message = Buffer.concat([
      uint64LE(nonce),
      uint32LE(timestamp),
      uint32LE(ONEGATE_NETWORK),
      this.identity.scriptHashBytes(),
      varString(payload.action),
      varString(payload.domain),
    ]);
    return {
      algorithm: "ECDSA-P256",
      network: ONEGATE_NETWORK,
      pubkey: this.identity.pubkey,
      address: this.identity.address,
      nonce: payload.nonce,
      timestamp,
      signature: this.identity.sign(message).toString("base64"),
    };
  }

  #signContext(rawContext) {
    const { context, hash } = this.#validateContext(rawContext);
    const signature = this.identity.sign(Buffer.concat([uint32LE(ONEGATE_NETWORK), hash])).toString("base64");
    const items = clone(context.items ?? {});
    items[this.identity.hash] = {
      script: this.identity.verificationScript,
      parameters: [{ type: "Signature", value: signature }],
      signatures: { [this.identity.pubkey]: signature },
    };
    return { ...clone(context), items };
  }

  #validateContext(rawContext) {
    const context = requireObject(rawContext, "context");
    if (typeof context.type !== "string" || !context.type.endsWith(".Transaction")) {
      throw new DapiEngineError(DapiErrorCode.UNSUPPORTED, "Only transaction signing is supported.");
    }
    if (context.network !== ONEGATE_NETWORK) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Transaction context network mismatch.");
    }
    const transaction = decodeBase64(context.data, "context.data");
    const hash = sha256(transaction);
    const expectedHash = displayUInt256(hash);
    if (context.hash !== expectedHash) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Transaction context hash mismatch.");
    }
    return { context, hash };
  }

  #send(args) {
    requireString(args[0], "asset");
    const from = args[1];
    if (from !== undefined && from !== null && from !== this.identity.hash) {
      throw new DapiEngineError(DapiErrorCode.NOTFOUND, "Account not found.");
    }
    requireString(args[2], "to");
    if (typeof args[3] !== "string" && typeof args[3] !== "number") {
      throw new DapiEngineError(DapiErrorCode.INVALID, "amount must be a string or number.");
    }
    if (this.transactionMode === "offline") {
      throw new DapiEngineError(DapiErrorCode.INSUFFICIENT_FUNDS, "The development account has insufficient funds.");
    }
    return this.#simulatedTransactionHash("send", args);
  }

  #invokeTransaction(method, args) {
    if (!Array.isArray(args[0]) || args[0].length === 0) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "invocations must be a non-empty array.");
    }
    if (this.transactionMode === "offline") {
      throw new DapiEngineError(DapiErrorCode.INSUFFICIENT_FUNDS, "The development account has insufficient funds.");
    }
    return this.#simulatedTransactionHash(method, args);
  }

  #makeTransaction(args) {
    if (!Array.isArray(args[0]) || args[0].length === 0) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "invocations must be a non-empty array.");
    }
    if (this.transactionMode === "offline") this.#rpcUnavailable("makeTransaction");
    const data = Buffer.concat([
      Buffer.from("OneGate Codex simulated transaction\0", "utf8"),
      randomBytes(16),
      Buffer.from(JSON.stringify(args), "utf8"),
    ]);
    return {
      type: "Neo.Network.P2P.Payloads.Transaction",
      hash: displayUInt256(sha256(data)),
      data: data.toString("base64"),
      items: {},
      network: ONEGATE_NETWORK,
    };
  }

  #relay(rawContext) {
    if (this.transactionMode === "offline") this.#rpcUnavailable("relay");
    const { context, hash } = this.#validateContext(rawContext);
    const item = context.items?.[this.identity.hash];
    const encodedSignature = item?.signatures?.[this.identity.pubkey] ?? item?.parameters?.[0]?.value;
    if (typeof encodedSignature !== "string") {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Context is not fully signed.");
    }
    const signature = decodeBase64(encodedSignature, "context signature");
    if (!verifyP256Signature(
      Buffer.concat([uint32LE(ONEGATE_NETWORK), hash]),
      signature,
      Buffer.from(this.identity.pubkey, "hex"),
    )) {
      throw new DapiEngineError(DapiErrorCode.INVALID, "Transaction context signature is invalid.");
    }
    return context.hash;
  }

  #simulatedTransactionHash(method, args) {
    const request = Buffer.from(JSON.stringify({ method, args }), "utf8");
    const signature = this.identity.sign(request);
    return displayUInt256(sha256(Buffer.concat([request, signature])));
  }

  #rpcUnavailable(method) {
    throw new DapiEngineError(
      DapiErrorCode.RPC_ERROR,
      `${method} requires an RPC connection; the Browser Mock is running in offline mode.`,
    );
  }
}
