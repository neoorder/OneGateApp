import { spawn } from "node:child_process";
import { closeSync, openSync } from "node:fs";
import { mkdir, open, rm, stat } from "node:fs/promises";
import { fileURLToPath } from "node:url";

import {
  daemonPaths,
  publicDaemonDescriptor,
  readDaemonDescriptor,
  removeDaemonDescriptor,
  runtimeStateDirectory,
} from "./daemon-protocol.mjs";

const DAEMON_ENTRYPOINT = fileURLToPath(new URL("../daemon.mjs", import.meta.url));
const START_TIMEOUT_MS = 12_000;
const REQUEST_TIMEOUT_MS = 30_000;

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function request(descriptor, pathname, options = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs ?? REQUEST_TIMEOUT_MS);
  try {
    const response = await fetch(`http://127.0.0.1:${descriptor.port}${pathname}`, {
      method: options.method ?? "GET",
      headers: {
        Authorization: `Bearer ${descriptor.authToken}`,
        ...(options.body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: controller.signal,
    });
    const payload = await response.json().catch(() => undefined);
    if (!response.ok || payload?.ok !== true) {
      const error = new Error(payload?.error?.message ?? `OneGate daemon returned HTTP ${response.status}.`);
      error.code = payload?.error?.code ?? "DAEMON_REQUEST_FAILED";
      throw error;
    }
    return payload.result;
  } finally {
    clearTimeout(timeout);
  }
}

export async function inspectDaemon(stateDirectory = runtimeStateDirectory()) {
  let descriptor;
  try {
    descriptor = await readDaemonDescriptor(stateDirectory);
  } catch {
    await removeDaemonDescriptor(stateDirectory).catch(() => undefined);
    return { running: false, stateDirectory };
  }
  if (!descriptor) return { running: false, stateDirectory };
  try {
    const health = await request(descriptor, "/health", { timeoutMs: 1_500 });
    return {
      running: true,
      ...publicDaemonDescriptor(descriptor, stateDirectory),
      sessionCount: health.sessionCount,
    };
  } catch {
    await removeDaemonDescriptor(stateDirectory, descriptor.pid).catch(() => undefined);
    return { running: false, stateDirectory };
  }
}

async function healthyDescriptor(stateDirectory) {
  const status = await inspectDaemon(stateDirectory);
  if (!status.running) return undefined;
  return readDaemonDescriptor(stateDirectory);
}

async function acquireStartLock(stateDirectory) {
  const { lockPath } = daemonPaths(stateDirectory);
  await mkdir(stateDirectory, { recursive: true, mode: 0o700 });
  const deadline = Date.now() + START_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      return await open(lockPath, "wx", 0o600);
    } catch (error) {
      if (error?.code !== "EEXIST") throw error;
      const descriptor = await healthyDescriptor(stateDirectory);
      if (descriptor) return undefined;
      const age = Date.now() - (await stat(lockPath).catch(() => ({ mtimeMs: 0 }))).mtimeMs;
      if (age > START_TIMEOUT_MS) {
        await rm(lockPath, { force: true });
        continue;
      }
      await delay(100);
    }
  }
  throw new Error("Timed out waiting for the OneGate daemon startup lock.");
}

export async function ensureDaemon(stateDirectory = runtimeStateDirectory()) {
  const existing = await healthyDescriptor(stateDirectory);
  if (existing) return existing;

  const lock = await acquireStartLock(stateDirectory);
  if (!lock) {
    const descriptor = await healthyDescriptor(stateDirectory);
    if (descriptor) return descriptor;
    throw new Error("The OneGate daemon startup lock was released without a running daemon.");
  }

  const { lockPath, logPath } = daemonPaths(stateDirectory);
  try {
    const raced = await healthyDescriptor(stateDirectory);
    if (raced) return raced;

    const logHandle = openSync(logPath, "a", 0o600);
    try {
      const child = spawn(process.execPath, [DAEMON_ENTRYPOINT, "--state-dir", stateDirectory], {
        detached: true,
        windowsHide: true,
        stdio: ["ignore", logHandle, logHandle],
        env: { ...process.env, ONEGATE_PLUGIN_STATE_DIR: stateDirectory },
      });
      child.unref();
    } finally {
      closeSync(logHandle);
    }

    const deadline = Date.now() + START_TIMEOUT_MS;
    while (Date.now() < deadline) {
      const descriptor = await healthyDescriptor(stateDirectory);
      if (descriptor) return descriptor;
      await delay(100);
    }
    throw new Error(`The OneGate daemon did not start. Inspect ${logPath}.`);
  } finally {
    await lock.close().catch(() => undefined);
    await rm(lockPath, { force: true });
  }
}

export async function sendDaemonCommand(
  command,
  args = {},
  options = {},
) {
  const stateDirectory = options.stateDirectory ?? runtimeStateDirectory();
  const descriptor = options.start === false
    ? await healthyDescriptor(stateDirectory)
    : await ensureDaemon(stateDirectory);
  if (!descriptor) {
    const error = new Error("The OneGate daemon is not running.");
    error.code = "DAEMON_NOT_RUNNING";
    throw error;
  }
  return request(descriptor, "/command", {
    method: "POST",
    body: { command, args },
    timeoutMs: options.timeoutMs,
  });
}
