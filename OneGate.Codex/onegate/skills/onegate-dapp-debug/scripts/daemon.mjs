import { randomBytes, timingSafeEqual } from "node:crypto";
import http from "node:http";
import path from "node:path";

import { OneGateCommandService, RuntimeCommandError } from "./runtime/command-service.mjs";
import {
  daemonPaths,
  removeDaemonDescriptor,
  runtimeStateDirectory,
  writeDaemonDescriptor,
} from "./runtime/daemon-protocol.mjs";
import { ONEGATE_RUNTIME_VERSION } from "./runtime/identity.mjs";

function readStateDirectory(argv) {
  const index = argv.indexOf("--state-dir");
  if (index === -1) return runtimeStateDirectory();
  const value = argv[index + 1];
  if (!value) throw new Error("--state-dir requires a path.");
  return path.resolve(value);
}

function sendJson(response, status, payload) {
  const body = Buffer.from(JSON.stringify(payload), "utf8");
  response.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": body.length,
    "Cache-Control": "no-store",
  });
  response.end(body);
}

function authorized(request, token) {
  const value = request.headers.authorization;
  if (typeof value !== "string" || !value.startsWith("Bearer ")) return false;
  const supplied = Buffer.from(value.slice(7), "utf8");
  const expected = Buffer.from(token, "utf8");
  return supplied.length === expected.length && timingSafeEqual(supplied, expected);
}

async function readJsonBody(request) {
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > 1_048_576) {
      throw new RuntimeCommandError("REQUEST_TOO_LARGE", "Daemon requests are limited to 1 MiB.");
    }
    chunks.push(chunk);
  }
  if (chunks.length === 0) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch (error) {
    throw new RuntimeCommandError("INVALID_JSON", "The daemon request body is not valid JSON.", {
      cause: error,
    });
  }
}

const stateDirectory = readStateDirectory(process.argv.slice(2));
process.env.ONEGATE_PLUGIN_STATE_DIR = stateDirectory;
const service = new OneGateCommandService({ stateDirectory });
const authToken = randomBytes(32).toString("hex");
const startedAt = new Date().toISOString();
let shuttingDown = false;

const server = http.createServer((request, response) => {
  void (async () => {
    if (!authorized(request, authToken)) {
      sendJson(response, 401, {
        ok: false,
        error: { code: "UNAUTHORIZED", message: "A valid daemon token is required." },
      });
      return;
    }
    if (request.method === "GET" && request.url === "/health") {
      sendJson(response, 200, {
        ok: true,
        result: {
          version: ONEGATE_RUNTIME_VERSION,
          pid: process.pid,
          sessionCount: service.sessionCount,
        },
      });
      return;
    }
    if (request.method !== "POST" || request.url !== "/command") {
      sendJson(response, 404, {
        ok: false,
        error: { code: "NOT_FOUND", message: "Daemon endpoint not found." },
      });
      return;
    }
    const message = await readJsonBody(request);
    if (message?.command === "daemon.stop") {
      if (service.sessionCount !== 0 && message?.args?.force !== true) {
        throw new RuntimeCommandError(
          "ACTIVE_SESSIONS",
          "The daemon has active sessions. Stop them first or pass force: true.",
        );
      }
      sendJson(response, 200, {
        ok: true,
        result: { pid: process.pid, stopping: true, sessionCount: service.sessionCount },
      });
      setTimeout(() => void shutdown(0), 10);
      return;
    }
    const result = await service.execute(message?.command, message?.args);
    sendJson(response, 200, { ok: true, result });
  })().catch((error) => {
    sendJson(response, 400, {
      ok: false,
      error: {
        code: error?.code ?? "RUNTIME_ERROR",
        message: error instanceof Error ? error.message : String(error),
      },
    });
  });
});

async function shutdown(exitCode) {
  if (shuttingDown) return;
  shuttingDown = true;
  await service.stopAll();
  await new Promise((resolve) => server.close(resolve));
  await removeDaemonDescriptor(stateDirectory, process.pid).catch(() => undefined);
  process.exit(exitCode);
}

server.on("error", (error) => {
  console.error(error instanceof Error ? error.stack : String(error));
  void shutdown(1);
});

server.listen(0, "127.0.0.1", async () => {
  const address = server.address();
  if (!address || typeof address === "string") {
    throw new Error("The OneGate daemon did not receive a loopback TCP port.");
  }
  await writeDaemonDescriptor({
    schemaVersion: 1,
    version: ONEGATE_RUNTIME_VERSION,
    pid: process.pid,
    port: address.port,
    authToken,
    startedAt,
  }, stateDirectory);
  const { descriptorPath } = daemonPaths(stateDirectory);
  console.error(`OneGate daemon ${process.pid} listening on 127.0.0.1:${address.port}; ${descriptorPath}`);
});

process.on("SIGINT", () => void shutdown(0));
process.on("SIGTERM", () => void shutdown(0));
process.on("uncaughtException", (error) => {
  console.error(error instanceof Error ? error.stack : String(error));
  void shutdown(1);
});
process.on("unhandledRejection", (error) => {
  console.error(error instanceof Error ? error.stack : String(error));
  void shutdown(1);
});
