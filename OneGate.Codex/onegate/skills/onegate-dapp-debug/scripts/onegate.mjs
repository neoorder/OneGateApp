#!/usr/bin/env node

import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";

import {
  inspectDaemon,
  sendDaemonCommand,
} from "./runtime/daemon-client.mjs";
import { runtimeStateDirectory } from "./runtime/daemon-protocol.mjs";

const BOOLEAN_OPTIONS = new Set([
  "confirm",
  "force",
  "headless",
  "ignore-cache",
]);

function requireCompatibleNode() {
  const major = Number.parseInt(process.versions.node.split(".")[0], 10);
  if (major < 22 || typeof WebSocket !== "function") {
    throw commandError(
      "UNSUPPORTED_NODE",
      `OneGate requires Node.js 22 or newer with built-in WebSocket support; received ${process.version}.`,
    );
  }
}

function parseOptions(tokens) {
  const options = {};
  for (let index = 0; index < tokens.length; index += 1) {
    const token = tokens[index];
    if (!token.startsWith("--")) {
      throw commandError("INVALID_ARGUMENT", `Unexpected positional argument: ${token}`);
    }
    const equals = token.indexOf("=");
    const name = token.slice(2, equals === -1 ? undefined : equals);
    if (!name) throw commandError("INVALID_ARGUMENT", "Option names cannot be empty.");
    if (equals !== -1) {
      options[name] = token.slice(equals + 1);
      continue;
    }
    if (BOOLEAN_OPTIONS.has(name)) {
      options[name] = true;
      continue;
    }
    const value = tokens[index + 1];
    if (value === undefined || value.startsWith("--")) {
      throw commandError("INVALID_ARGUMENT", `--${name} requires a value.`);
    }
    options[name] = value;
    index += 1;
  }
  return options;
}

function commandError(code, message) {
  const error = new Error(message);
  error.code = code;
  return error;
}

function requireOption(options, name) {
  const value = options[name];
  if (typeof value !== "string" || value.length === 0) {
    throw commandError("INVALID_ARGUMENT", `--${name} is required.`);
  }
  return value;
}

function allowOnly(options, names) {
  const allowed = new Set(names);
  for (const name of Object.keys(options)) {
    if (!allowed.has(name)) {
      throw commandError("INVALID_ARGUMENT", `Unknown option: --${name}`);
    }
  }
}

function parseInteger(value, name) {
  if (value === undefined) return undefined;
  if (!/^\d+$/u.test(value)) {
    throw commandError("INVALID_ARGUMENT", `--${name} must be a non-negative integer.`);
  }
  return Number.parseInt(value, 10);
}

function usage() {
  return {
    version: "1.0.1",
    commands: [
      "doctor",
      "targets discover",
      "identity [show]",
      "identity regenerate --confirm",
      "review start [--browser-executable <path>] [--headless]",
      "debug start --url <url> [--target browser] [--browser-executable <path>] [--profile <path>] [--headless]",
      "session list",
      "session status --id <session-id>",
      "session logs --id <session-id> [--after-sequence <n>]",
      "session trace --id <session-id>",
      "session screenshot --id <session-id> --output <png-path>",
      "session evaluate --id <session-id> --expression <javascript>",
      "session reload --id <session-id> [--ignore-cache]",
      "session stop --id <session-id>",
      "daemon status",
      "daemon stop [--force]",
    ],
  };
}

function resolveCommand(tokens) {
  const [group, action, ...rest] = tokens;
  if (!group || group === "help" || group === "--help" || group === "-h") {
    return { display: "help", local: "help", options: {} };
  }
  if (group === "doctor") {
    return { display: "doctor", runtime: "doctor", options: parseOptions(tokens.slice(1)) };
  }
  if (group === "targets" && action === "discover") {
    return { display: "targets discover", runtime: "targets.discover", options: parseOptions(rest) };
  }
  if (group === "identity" && (action === undefined || action === "show")) {
    return {
      display: action ? "identity show" : "identity",
      runtime: "identity.get",
      options: parseOptions(action ? rest : tokens.slice(1)),
    };
  }
  if (group === "identity" && action === "regenerate") {
    return { display: "identity regenerate", runtime: "identity.regenerate", options: parseOptions(rest) };
  }
  if (group === "review" && action === "start") {
    return { display: "review start", runtime: "review.start", options: parseOptions(rest) };
  }
  if (group === "debug" && action === "start") {
    return { display: "debug start", runtime: "debug.start", options: parseOptions(rest) };
  }
  if (group === "session" && [
    "list",
    "status",
    "logs",
    "trace",
    "screenshot",
    "evaluate",
    "reload",
    "stop",
  ].includes(action)) {
    return { display: `session ${action}`, runtime: `session.${action}`, options: parseOptions(rest) };
  }
  if (group === "daemon" && ["status", "stop"].includes(action)) {
    return { display: `daemon ${action}`, local: `daemon.${action}`, options: parseOptions(rest) };
  }
  throw commandError("UNKNOWN_COMMAND", `Unknown OneGate command: ${tokens.join(" ")}`);
}

function runtimeArguments(command) {
  const options = command.options;
  switch (command.runtime) {
    case "doctor":
    case "targets.discover":
    case "identity.get":
    case "session.list": {
      allowOnly(options, []);
      return {};
    }
    case "identity.regenerate": {
      allowOnly(options, ["confirm"]);
      return { confirm: options.confirm === true };
    }
    case "review.start": {
      allowOnly(options, ["browser-executable", "headless"]);
      return {
        browserExecutable: options["browser-executable"],
        headless: options.headless === true,
      };
    }
    case "debug.start": {
      allowOnly(options, ["target", "url", "browser-executable", "profile", "headless"]);
      return {
        target: options.target ?? "browser",
        url: requireOption(options, "url"),
        browserExecutable: options["browser-executable"],
        profilePath: options.profile,
        headless: options.headless === true,
      };
    }
    case "session.status":
    case "session.trace":
    case "session.stop": {
      allowOnly(options, ["id"]);
      return { sessionId: requireOption(options, "id") };
    }
    case "session.screenshot": {
      allowOnly(options, ["id", "output"]);
      requireOption(options, "output");
      return { sessionId: requireOption(options, "id") };
    }
    case "session.logs": {
      allowOnly(options, ["id", "after-sequence"]);
      return {
        sessionId: requireOption(options, "id"),
        afterSequence: parseInteger(options["after-sequence"], "after-sequence") ?? 0,
      };
    }
    case "session.evaluate": {
      allowOnly(options, ["id", "expression"]);
      return {
        sessionId: requireOption(options, "id"),
        expression: requireOption(options, "expression"),
      };
    }
    case "session.reload": {
      allowOnly(options, ["id", "ignore-cache"]);
      return {
        sessionId: requireOption(options, "id"),
        ignoreCache: options["ignore-cache"] === true,
      };
    }
    default:
      throw commandError("UNKNOWN_COMMAND", `Unknown runtime command: ${command.runtime}`);
  }
}

async function execute(command) {
  if (command.local === "help") return usage();
  if (command.local === "daemon.status") {
    allowOnly(command.options, []);
    return inspectDaemon(runtimeStateDirectory());
  }
  if (command.local === "daemon.stop") {
    allowOnly(command.options, ["force"]);
    return sendDaemonCommand("daemon.stop", { force: command.options.force === true }, {
      start: false,
      timeoutMs: 10_000,
    });
  }

  const args = runtimeArguments(command);
  const result = await sendDaemonCommand(command.runtime, args);
  if (command.runtime !== "session.screenshot") return result;

  const output = path.resolve(requireOption(command.options, "output"));
  await mkdir(path.dirname(output), { recursive: true });
  const data = Buffer.from(result.data, "base64");
  await writeFile(output, data);
  return {
    sessionId: result.sessionId,
    mimeType: result.mimeType,
    output,
    bytes: data.length,
  };
}

const rawTokens = process.argv.slice(2);
let display = rawTokens.join(" ") || "help";
try {
  requireCompatibleNode();
  const command = resolveCommand(rawTokens);
  display = command.display;
  const result = await execute(command);
  process.stdout.write(`${JSON.stringify({ ok: true, command: display, result })}\n`);
} catch (error) {
  process.stdout.write(`${JSON.stringify({
    ok: false,
    command: display,
    error: {
      code: error?.code ?? "ONEGATE_ERROR",
      message: error instanceof Error ? error.message : String(error),
    },
  })}\n`);
  process.exitCode = 1;
}
