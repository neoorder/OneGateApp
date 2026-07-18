import { BrowserMockSession, discoverBrowsers } from "./browser-session.mjs";
import {
  IdentityStore,
  ONEGATE_RUNTIME_VERSION,
  defaultIdentityDirectory,
} from "./identity.mjs";
import { ReviewerFixtureServer } from "./reviewer-fixture.mjs";

export class RuntimeCommandError extends Error {
  constructor(code, message, options) {
    super(message, options);
    this.name = "RuntimeCommandError";
    this.code = code;
  }
}

function requireObject(value) {
  if (value === undefined) return {};
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new RuntimeCommandError("INVALID_ARGUMENT", "Command arguments must be an object.");
  }
  return value;
}

function requireString(value, name, maximumLength = 100_000) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new RuntimeCommandError("INVALID_ARGUMENT", `${name} must be a non-empty string.`);
  }
  if (value.length > maximumLength) {
    throw new RuntimeCommandError(
      "INVALID_ARGUMENT",
      `${name} must not exceed ${maximumLength} characters.`,
    );
  }
  return value;
}

export class OneGateCommandService {
  constructor(options = {}) {
    this.stateDirectory = options.stateDirectory ?? defaultIdentityDirectory();
    this.identityStore = options.identityStore ?? new IdentityStore({
      directory: this.stateDirectory,
    });
    this.sessions = new Map();
    this.reviewerFixtures = new Map();
  }

  get sessionCount() {
    return this.sessions.size;
  }

  getSession(sessionId) {
    const id = requireString(sessionId, "sessionId", 200);
    const session = this.sessions.get(id);
    if (!session) {
      throw new RuntimeCommandError(
        "SESSION_NOT_FOUND",
        `Browser Mock session was not found: ${id}`,
      );
    }
    return session;
  }

  async execute(command, rawArguments) {
    const args = requireObject(rawArguments);
    switch (command) {
      case "doctor": {
        const browsers = await discoverBrowsers();
        return {
          version: ONEGATE_RUNTIME_VERSION,
          nodeVersion: process.version,
          platform: process.platform,
          architecture: process.arch,
          stateDirectory: this.stateDirectory,
          browserMock: {
            available: browsers.length > 0,
            browsers,
          },
        };
      }
      case "targets.discover": {
        const browsers = await discoverBrowsers();
        return {
          targets: [
            {
              id: "browser",
              kind: "browser-mock",
              name: "Browser Mock",
              implemented: true,
              available: browsers.length > 0,
              browsers,
            },
          ],
        };
      }
      case "identity.get":
        return this.identityStore.publicSummary();
      case "identity.regenerate": {
        if (args.confirm !== true) {
          throw new RuntimeCommandError(
            "CONFIRMATION_REQUIRED",
            "confirm must be true to regenerate the development identity.",
          );
        }
        if (this.sessions.size !== 0) {
          throw new RuntimeCommandError(
            "ACTIVE_SESSIONS",
            "Stop every Browser Mock session before regenerating the development identity.",
          );
        }
        const identity = await this.identityStore.regenerate();
        return identity.publicSummary(this.identityStore.filePath);
      }
      case "review.start": {
        const fixture = await ReviewerFixtureServer.start();
        let session;
        try {
          session = await BrowserMockSession.start({
            url: fixture.url,
            browserExecutable: args.browserExecutable,
            headless: args.headless === true,
            identity: await this.identityStore.load(),
          });
          this.sessions.set(session.sessionId, session);
          this.reviewerFixtures.set(session.sessionId, fixture);
          return {
            ...await session.status(),
            reviewerFixture: {
              bundled: true,
              url: fixture.url,
            },
          };
        } catch (error) {
          if (session) {
            this.sessions.delete(session.sessionId);
            this.reviewerFixtures.delete(session.sessionId);
            await session.stop().catch(() => undefined);
          }
          await fixture.stop().catch(() => undefined);
          throw error;
        }
      }
      case "debug.start": {
        const target = args.target ?? "browser";
        if (target !== "browser") {
          throw new RuntimeCommandError(
            "TARGET_NOT_IMPLEMENTED",
            `The debugging target is not implemented in version ${ONEGATE_RUNTIME_VERSION}: ${target}`,
          );
        }
        const session = await BrowserMockSession.start({
          url: requireString(args.url, "url", 8_192),
          profilePath: args.profilePath,
          browserExecutable: args.browserExecutable,
          headless: args.headless === true,
          identity: await this.identityStore.load(),
        });
        this.sessions.set(session.sessionId, session);
        try {
          return await session.status();
        } catch (error) {
          this.sessions.delete(session.sessionId);
          await session.stop().catch(() => undefined);
          throw error;
        }
      }
      case "session.list": {
        const sessions = await Promise.all(
          [...this.sessions.values()].map((session) => session.status()),
        );
        return { sessions };
      }
      case "session.status":
        return this.getSession(args.sessionId).status();
      case "session.logs":
        return {
          sessionId: requireString(args.sessionId, "sessionId", 200),
          entries: this.getSession(args.sessionId).getLogs(args.afterSequence ?? 0),
        };
      case "session.trace":
        return {
          sessionId: requireString(args.sessionId, "sessionId", 200),
          entries: await this.getSession(args.sessionId).getTrace(),
        };
      case "session.screenshot":
        return {
          sessionId: requireString(args.sessionId, "sessionId", 200),
          mimeType: "image/png",
          data: await this.getSession(args.sessionId).screenshot(),
        };
      case "session.evaluate":
        return {
          sessionId: requireString(args.sessionId, "sessionId", 200),
          value: await this.getSession(args.sessionId).evaluate(
            requireString(args.expression, "expression"),
          ),
        };
      case "session.reload":
        return this.getSession(args.sessionId).reload(args.ignoreCache === true);
      case "session.stop": {
        const sessionId = requireString(args.sessionId, "sessionId", 200);
        const session = this.getSession(sessionId);
        const fixture = this.reviewerFixtures.get(sessionId);
        try {
          await session.stop();
        } finally {
          this.sessions.delete(sessionId);
          this.reviewerFixtures.delete(sessionId);
          await fixture?.stop().catch(() => undefined);
        }
        return { sessionId, stopped: true };
      }
      default:
        throw new RuntimeCommandError("UNKNOWN_COMMAND", `Unknown runtime command: ${command}`);
    }
  }

  async stopAll() {
    await Promise.allSettled([
      ...[...this.sessions.values()].map((session) => session.stop()),
      ...[...this.reviewerFixtures.values()].map((fixture) => fixture.stop()),
    ]);
    this.sessions.clear();
    this.reviewerFixtures.clear();
  }
}
