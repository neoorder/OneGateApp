import { randomBytes } from "node:crypto";
import { chmod, mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import path from "node:path";

import { defaultIdentityDirectory, ONEGATE_RUNTIME_VERSION } from "./identity.mjs";

export const DAEMON_DESCRIPTOR_FILE = "daemon.json";
export const DAEMON_LOCK_FILE = "daemon-start.lock";
export const DAEMON_LOG_FILE = "daemon.log";

export function runtimeStateDirectory() {
  return path.resolve(defaultIdentityDirectory());
}

export function daemonPaths(stateDirectory = runtimeStateDirectory()) {
  const directory = path.resolve(stateDirectory);
  return {
    stateDirectory: directory,
    descriptorPath: path.join(directory, DAEMON_DESCRIPTOR_FILE),
    lockPath: path.join(directory, DAEMON_LOCK_FILE),
    logPath: path.join(directory, DAEMON_LOG_FILE),
  };
}

export async function readDaemonDescriptor(stateDirectory = runtimeStateDirectory()) {
  const { descriptorPath } = daemonPaths(stateDirectory);
  let value;
  try {
    value = JSON.parse(await readFile(descriptorPath, "utf8"));
  } catch (error) {
    if (error?.code === "ENOENT") return undefined;
    throw new Error(`The OneGate daemon descriptor is invalid: ${descriptorPath}`, {
      cause: error,
    });
  }
  if (
    value?.schemaVersion !== 1
    || value?.version !== ONEGATE_RUNTIME_VERSION
    || !Number.isInteger(value?.pid)
    || !Number.isInteger(value?.port)
    || value.port < 1
    || value.port > 65535
    || typeof value?.authToken !== "string"
    || !/^[0-9a-f]{64}$/u.test(value.authToken)
  ) {
    throw new Error(`The OneGate daemon descriptor is invalid: ${descriptorPath}`);
  }
  return value;
}

export async function writeDaemonDescriptor(
  descriptor,
  stateDirectory = runtimeStateDirectory(),
) {
  const { descriptorPath } = daemonPaths(stateDirectory);
  await mkdir(stateDirectory, { recursive: true, mode: 0o700 });
  const temporaryPath = `${descriptorPath}.${process.pid}.${randomBytes(6).toString("hex")}.tmp`;
  await writeFile(temporaryPath, `${JSON.stringify(descriptor, null, 2)}\n`, {
    encoding: "utf8",
    mode: 0o600,
  });
  await rename(temporaryPath, descriptorPath);
  await chmod(descriptorPath, 0o600).catch(() => undefined);
  return descriptorPath;
}

export async function removeDaemonDescriptor(
  stateDirectory = runtimeStateDirectory(),
  expectedPid,
) {
  const { descriptorPath } = daemonPaths(stateDirectory);
  if (expectedPid !== undefined) {
    try {
      const descriptor = await readDaemonDescriptor(stateDirectory);
      if (descriptor?.pid !== expectedPid) return false;
    } catch {
      // A corrupt descriptor is safe to remove when the caller owns this state directory.
    }
  }
  await rm(descriptorPath, { force: true });
  return true;
}

export function publicDaemonDescriptor(descriptor, stateDirectory = runtimeStateDirectory()) {
  if (!descriptor) return undefined;
  const { descriptorPath, logPath } = daemonPaths(stateDirectory);
  return {
    schemaVersion: descriptor.schemaVersion,
    version: descriptor.version,
    pid: descriptor.pid,
    host: "127.0.0.1",
    port: descriptor.port,
    startedAt: descriptor.startedAt,
    stateDirectory: path.resolve(stateDirectory),
    descriptorPath,
    logPath,
  };
}
