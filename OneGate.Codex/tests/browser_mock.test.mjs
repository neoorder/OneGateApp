import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import {
  createInjectionSource,
  discoverBrowsers,
} from "../onegate/skills/onegate-dapp-debug/scripts/runtime/browser-session.mjs";
import { DapiEngine } from "../onegate/skills/onegate-dapp-debug/scripts/runtime/dapi-engine.mjs";
import {
  createDevelopmentIdentity,
  IdentityStore,
  ONEGATE_NETWORK,
  verifyP256Signature,
} from "../onegate/skills/onegate-dapp-debug/scripts/runtime/identity.mjs";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const skillDirectory = path.resolve(
  testDirectory,
  "../onegate/skills/onegate-dapp-debug",
);
const cliPath = path.join(skillDirectory, "scripts/onegate.mjs");
const posixLauncherPath = path.join(skillDirectory, "scripts/onegate.sh");
const fixedPrivateKey = Buffer.from(
  "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20",
  "hex",
);

function sha256(value) {
  return createHash("sha256").update(value).digest();
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

function varString(value) {
  const bytes = Buffer.from(value, "utf8");
  assert.ok(bytes.length < 0xfd, "Test vector requires a single-byte varuint length.");
  return Buffer.concat([Buffer.from([bytes.length]), bytes]);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function runCli(args, stateDirectory, timeoutMs = 45_000) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [cliPath, ...args], {
      cwd: skillDirectory,
      env: { ...process.env, ONEGATE_PLUGIN_STATE_DIR: stateDirectory },
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    const timeout = setTimeout(() => {
      child.kill();
      reject(new Error(`OneGate CLI timed out: ${args.join(" ")}`));
    }, timeoutMs);
    child.once("error", (error) => {
      clearTimeout(timeout);
      reject(error);
    });
    child.once("exit", (exitCode) => {
      clearTimeout(timeout);
      const output = stdout.trim();
      let payload;
      try {
        assert.equal(output.split(/\r?\n/u).length, 1);
        payload = JSON.parse(output);
      } catch (error) {
        reject(new Error(
          `OneGate CLI did not return one JSON envelope. stdout=${JSON.stringify(stdout)} stderr=${JSON.stringify(stderr)}`,
          { cause: error },
        ));
        return;
      }
      resolve({ exitCode, payload, stderr });
    });
  });
}

async function stopDaemon(stateDirectory) {
  await runCli(["daemon", "stop", "--force"], stateDirectory, 10_000).catch(() => undefined);
  const deadline = Date.now() + 5_000;
  while (Date.now() < deadline) {
    const status = await runCli(["daemon", "status"], stateDirectory, 5_000);
    if (status.payload.result?.running === false) return;
    await delay(50);
  }
  throw new Error("The OneGate test daemon did not stop.");
}

test("platform launcher accepts an explicit compatible Node runtime", async () => {
  const isWindows = process.platform === "win32";
  const executable = isWindows ? (process.env.ComSpec ?? "cmd.exe") : "sh";
  const args = isWindows
    ? ["/d", "/c", "call scripts\\onegate.cmd help"]
    : [posixLauncherPath, "help"];
  const result = await new Promise((resolve, reject) => {
    const child = spawn(executable, args, {
      cwd: skillDirectory,
      env: { ...process.env, ONEGATE_NODE: process.execPath },
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    child.once("error", reject);
    child.once("exit", (exitCode) => resolve({ exitCode, stdout, stderr }));
  });
  assert.equal(result.exitCode, 0, result.stderr);
  const envelope = JSON.parse(result.stdout.trim());
  assert.equal(envelope.ok, true);
  assert.equal(envelope.result.version, "1.0.1");
});

test("document-start source is top-level-only and carries public configuration", () => {
  const source = createInjectionSource(
    "test-session",
    { id: "portable", transactionMode: "offline", provider: {} },
    "window.__mockSourceExecuted = true;",
  );
  assert.match(source, /globalThis\.top === globalThis/u);
  assert.match(source, /__ONEGATE_MOCK_CONFIG__/u);
  assert.match(source, /test-session/u);
  assert.match(source, /window\.__mockSourceExecuted = true/u);
  assert.doesNotMatch(source, /privateKey/u);
});

test("development identity matches Neo N3 vectors and creates valid P-256 signatures", () => {
  const identity = createDevelopmentIdentity(fixedPrivateKey, "2026-07-16T00:00:00.000Z");
  assert.equal(identity.address, "NgaxELHoZFpQWNwd74Fvq4wF3qz57WTfpp");
  assert.equal(identity.hash, "0x321344861b95ee75470a09a4f20c0c1dc6f0c1e2");
  assert.equal(
    identity.pubkey,
    "02515c3d6eb9e396b904d3feca7f54fdcd0cc1e997bf375dca515ad0a6c3b4035f",
  );
  assert.equal(
    identity.verificationScript,
    "DCECUVw9brnjlrkE0/7Kf1T9zQzB6Ze/N13KUVrQpsO0A19BVuezJw==",
  );
  const payload = Buffer.from("OneGate Browser Mock identity test", "utf8");
  const signature = identity.sign(payload);
  assert.equal(signature.length, 64);
  assert.equal(
    verifyP256Signature(payload, signature, Buffer.from(identity.pubkey, "hex")),
    true,
  );
  assert.equal(JSON.stringify(identity).includes("private"), false);
});

test("identity store persists one address until explicit regeneration", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "onegate-identity-test-"));
  try {
    const first = await new IdentityStore({ directory }).load();
    const second = await new IdentityStore({ directory }).load();
    assert.equal(second.address, first.address);
    assert.equal(second.hash, first.hash);
    const replacement = await new IdentityStore({ directory }).regenerate();
    assert.notEqual(replacement.address, first.address);
    assert.equal((await new IdentityStore({ directory }).load()).address, replacement.address);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("NEP-20 authentication signs the specified response-data layout", async () => {
  const identity = createDevelopmentIdentity(fixedPrivateKey, "2026-07-16T00:00:00.000Z");
  const engine = new DapiEngine({
    identity,
    profile: {
      id: "nep20",
      transactionMode: "offline",
      provider: { network: ONEGATE_NETWORK },
    },
  });
  const challenge = {
    action: "Authentication",
    grant_type: "Signature",
    allowed_algorithms: ["ECDSA-P256"],
    domain: "example.com",
    networks: [ONEGATE_NETWORK],
    nonce: "13458238842203010919",
    timestamp: Math.floor(Date.now() / 1000),
  };

  const response = await engine.invoke("authenticate", [challenge], { host: "EXAMPLE.COM" });
  assert.deepEqual(
    {
      algorithm: response.algorithm,
      network: response.network,
      pubkey: response.pubkey,
      address: response.address,
      nonce: response.nonce,
    },
    {
      algorithm: "ECDSA-P256",
      network: ONEGATE_NETWORK,
      pubkey: identity.pubkey,
      address: identity.address,
      nonce: challenge.nonce,
    },
  );

  const specifiedData = Buffer.concat([
    uint64LE(BigInt(challenge.nonce)),
    uint32LE(response.timestamp),
    uint32LE(ONEGATE_NETWORK),
    identity.scriptHashBytes(),
    varString(challenge.action),
    varString(challenge.domain),
  ]);
  const signature = Buffer.from(response.signature, "base64");
  assert.equal(
    verifyP256Signature(specifiedData, signature, Buffer.from(identity.pubkey, "hex")),
    true,
  );

  const previousIncorrectData = Buffer.concat([
    uint32LE(ONEGATE_NETWORK),
    uint64LE(BigInt(challenge.nonce)),
    uint32LE(response.timestamp),
    identity.scriptHashBytes(),
    varString(challenge.action),
    varString(challenge.domain),
  ]);
  assert.equal(
    verifyP256Signature(previousIncorrectData, signature, Buffer.from(identity.pubkey, "hex")),
    false,
  );
});

test("NEP-20 authentication rejects malformed challenges", async () => {
  const identity = createDevelopmentIdentity(fixedPrivateKey, "2026-07-16T00:00:00.000Z");
  const engine = new DapiEngine({ identity, profile: {} });
  const challenge = {
    action: "Authentication",
    grant_type: "Signature",
    allowed_algorithms: ["ECDSA-P256"],
    domain: "example.com",
    networks: [ONEGATE_NETWORK],
    nonce: "18446744073709551615",
    timestamp: Math.floor(Date.now() / 1000),
  };

  await assert.rejects(
    engine.invoke("authenticate", [{ ...challenge, nonce: true }], { host: challenge.domain }),
    (error) => error.code === 10002 && /unsigned 64-bit/u.test(error.message),
  );
  await assert.rejects(
    engine.invoke("authenticate", [{ ...challenge, nonce: "0x10" }], { host: challenge.domain }),
    (error) => error.code === 10002 && /unsigned 64-bit/u.test(error.message),
  );
  await assert.rejects(
    engine.invoke("authenticate", [{ ...challenge, timestamp: challenge.timestamp + 600 }], { host: challenge.domain }),
    (error) => error.code === 10002 && /clock tolerance/u.test(error.message),
  );
  await assert.rejects(
    engine.invoke("authenticate", [challenge], { host: "attacker.example" }),
    (error) => error.code === 10002 && /Domain mismatch/u.test(error.message),
  );
});

test("dAPI engine signs messages and transaction contexts while offline transfers fail naturally", async () => {
  const identity = createDevelopmentIdentity(fixedPrivateKey, "2026-07-16T00:00:00.000Z");
  const engine = new DapiEngine({
    identity,
    profile: {
      id: "unit",
      transactionMode: "offline",
      provider: { network: ONEGATE_NETWORK },
      balances: [],
      fixtures: [],
      errors: [],
    },
  });
  assert.deepEqual(await engine.invoke("getAccounts", []), [identity.account]);
  const signedMessage = await engine.invoke("signMessage", ["hello", identity.hash]);
  assert.equal(signedMessage.account, identity.hash);
  assert.equal(
    verifyP256Signature(
      Buffer.from("hello"),
      Buffer.from(signedMessage.signature, "base64"),
      Buffer.from(identity.pubkey, "hex"),
    ),
    true,
  );

  const transactionData = Buffer.from(
    "AAEAAAAAAAAAAAAAAAAAAAAAAAAAZAAAAAHiwfDGHQwM8qQJCkd17pUbhkQTMgEAAUA=",
    "base64",
  );
  const context = {
    type: "Neo.Network.P2P.Payloads.Transaction",
    hash: "0x2c5939afb0a7811e1d56a97c672c1f2318b9db974364dc352381018f51d4dd87",
    data: transactionData.toString("base64"),
    items: {},
    network: ONEGATE_NETWORK,
  };
  const signedContext = await engine.invoke("sign", [context]);
  const transactionSignature = Buffer.from(
    signedContext.items[identity.hash].parameters[0].value,
    "base64",
  );
  const network = Buffer.alloc(4);
  network.writeUInt32LE(ONEGATE_NETWORK);
  assert.equal(
    verifyP256Signature(
      Buffer.concat([network, sha256(transactionData)]),
      transactionSignature,
      Buffer.from(identity.pubkey, "hex"),
    ),
    true,
  );
  await assert.rejects(
    engine.invoke("send", ["0xasset", identity.hash, identity.hash, "1", null]),
    (error) => error.code === 10007,
  );
  await assert.rejects(
    engine.invoke("call", [{ hash: "0xcontract", operation: "symbol", args: [] }]),
    (error) => error.code === 10008,
  );
});

test("simulate mode provides explicit fake transaction success paths", async () => {
  const identity = createDevelopmentIdentity(fixedPrivateKey, "2026-07-16T00:00:00.000Z");
  const engine = new DapiEngine({
    identity,
    profile: {
      id: "simulate",
      transactionMode: "simulate",
      provider: { network: ONEGATE_NETWORK },
    },
  });
  const invocations = [{ hash: identity.hash, operation: "test", args: [] }];
  const context = await engine.invoke("makeTransaction", [invocations]);
  assert.equal(context.type, "Neo.Network.P2P.Payloads.Transaction");
  const transaction = await engine.invoke("invoke", [invocations]);
  assert.match(transaction, /^0x[0-9a-f]{64}$/u);
});

test("JSON CLI keeps identity and sessions in an authenticated local daemon", async () => {
  const stateDirectory = await mkdtemp(path.join(os.tmpdir(), "onegate-cli-test-"));
  try {
    const help = await runCli(["help"], stateDirectory);
    assert.equal(help.exitCode, 0);
    assert.equal(help.payload.ok, true);
    assert.equal(help.payload.result.version, "1.0.1");

    const first = await runCli(["identity"], stateDirectory);
    const second = await runCli(["identity", "show"], stateDirectory);
    assert.equal(first.exitCode, 0);
    assert.match(first.payload.result.address, /^N/u);
    assert.equal(second.payload.result.address, first.payload.result.address);
    assert.equal(JSON.stringify(first.payload).includes("privateKey"), false);

    const status = await runCli(["daemon", "status"], stateDirectory);
    assert.equal(status.payload.result.running, true);
    assert.equal(status.payload.result.sessionCount, 0);
    assert.equal(JSON.stringify(status.payload).includes("authToken"), false);
    const unauthorized = await fetch(
      `http://127.0.0.1:${status.payload.result.port}/health`,
    );
    assert.equal(unauthorized.status, 401);

    const rejected = await runCli(["identity", "regenerate"], stateDirectory);
    assert.equal(rejected.exitCode, 1);
    assert.equal(rejected.payload.error.code, "CONFIRMATION_REQUIRED");
  } finally {
    await stopDaemon(stateDirectory);
    await rm(stateDirectory, { recursive: true, force: true });
  }
});

test("bundled reviewer DApp starts through the CLI and signs at document start", async (context) => {
  const browsers = await discoverBrowsers();
  if (browsers.length === 0) {
    context.skip("No CDP-compatible Chromium browser is available in this environment.");
    return;
  }

  const stateDirectory = await mkdtemp(path.join(os.tmpdir(), "onegate-browser-cli-"));
  let targetUrl;
  let sessionId;

  try {
    context.diagnostic(`Using browser: ${browsers[0].executable}`);
    const identity = await runCli(["identity"], stateDirectory);
    const started = await runCli([
      "review",
      "start",
      "--browser-executable",
      browsers[0].executable,
      "--headless",
    ], stateDirectory);
    assert.equal(started.exitCode, 0, JSON.stringify(started.payload));
    const status = started.payload.result;
    targetUrl = status.reviewerFixture.url;
    sessionId = status.sessionId;
    assert.equal(status.reviewerFixture.bundled, true);
    assert.equal(status.origin, new URL(targetUrl).origin);
    assert.equal(status.href, targetUrl);
    assert.equal(status.mock, true);
    assert.equal(status.provider.extra.mock, true);
    assert.equal(status.provider.name, "OneGate Codex Plugin");
    assert.equal(status.identity.address, identity.payload.result.address);
    assert.equal(status.transactionMode, "offline");
    assert.equal(typeof status.injectionIdentifier, "string");

    let fixtureResult;
    const deadline = Date.now() + 5_000;
    while (Date.now() < deadline) {
      const evaluated = await runCli([
        "session",
        "evaluate",
        "--id",
        sessionId,
        "--expression",
        "window.__fixtureResult || null",
      ], stateDirectory);
      fixtureResult = evaluated.payload.result.value;
      if (fixtureResult) break;
      await delay(50);
    }
    assert.deepEqual(fixtureResult, {
      mock: true,
      profile: "default",
      transactionMode: "offline",
      providerName: "OneGate Codex Plugin",
      network: ONEGATE_NETWORK,
      account: identity.payload.result.address,
      accountHash: identity.payload.result.hash,
      balance: "0",
      signedAccount: identity.payload.result.hash,
      signatureLength: 88,
      sendCode: 10007,
      rpcCode: 10008,
      traceCount: 10,
    });

    const trace = await runCli(["session", "trace", "--id", sessionId], stateDirectory);
    assert.equal(trace.payload.result.entries.length, 10);
    assert.equal(trace.payload.result.entries[0].method, "getAccounts");
    assert.equal(trace.payload.result.entries.at(-1).phase, "reject");

    const logs = await runCli(["session", "logs", "--id", sessionId], stateDirectory);
    assert.match(
      logs.payload.result.entries.map((entry) => entry.message).join("\n"),
      /\[OneGate Mock\] Provider ready/u,
    );

    const screenshotPath = path.join(stateDirectory, "fixture.png");
    const screenshot = await runCli([
      "session",
      "screenshot",
      "--id",
      sessionId,
      "--output",
      screenshotPath,
    ], stateDirectory);
    assert.equal(screenshot.payload.result.output, screenshotPath);
    const png = await readFile(screenshotPath);
    assert.deepEqual([...png.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);

    const reloaded = await runCli([
      "session",
      "reload",
      "--id",
      sessionId,
      "--ignore-cache",
    ], stateDirectory);
    assert.equal(reloaded.payload.result.origin, new URL(targetUrl).origin);
    assert.equal(reloaded.payload.result.mock, true);

    const stopped = await runCli(["session", "stop", "--id", sessionId], stateDirectory);
    assert.equal(stopped.payload.result.stopped, true);
    sessionId = undefined;
    await assert.rejects(fetch(targetUrl));
  } finally {
    if (sessionId) {
      await runCli(["session", "stop", "--id", sessionId], stateDirectory).catch(() => undefined);
    }
    await stopDaemon(stateDirectory);
    await rm(stateDirectory, { recursive: true, force: true });
  }
});
