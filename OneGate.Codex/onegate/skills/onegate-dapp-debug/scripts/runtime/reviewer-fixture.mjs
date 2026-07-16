import { readFile } from "node:fs/promises";
import http from "node:http";

const FIXTURE_URL = new URL("../../assets/reviewer-fixture/index.html", import.meta.url);

function send(response, status, body, contentType = "text/plain; charset=utf-8") {
  response.writeHead(status, {
    "Content-Type": contentType,
    "Content-Length": body.length,
    "Cache-Control": "no-store",
    "Content-Security-Policy": "default-src 'none'; script-src 'unsafe-inline'",
    "Referrer-Policy": "no-referrer",
    "X-Content-Type-Options": "nosniff",
  });
  response.end(body);
}

export class ReviewerFixtureServer {
  static async start() {
    const html = await readFile(FIXTURE_URL);
    const server = http.createServer((request, response) => {
      const pathname = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
      if (request.method === "GET" && pathname === "/") {
        send(response, 200, html, "text/html; charset=utf-8");
        return;
      }
      send(response, 404, Buffer.from("Not found", "utf8"));
    });
    await new Promise((resolve, reject) => {
      server.once("error", reject);
      server.listen(0, "127.0.0.1", resolve);
    });
    const address = server.address();
    if (!address || typeof address === "string") {
      await new Promise((resolve) => server.close(resolve));
      throw new Error("The reviewer fixture did not receive a loopback TCP port.");
    }
    return new ReviewerFixtureServer(
      server,
      `http://127.0.0.1:${address.port}/?onegate-review=1#document-start`,
    );
  }

  constructor(server, url) {
    this.server = server;
    this.url = url;
    this.stopped = false;
  }

  async stop() {
    if (this.stopped) return;
    this.stopped = true;
    await new Promise((resolve) => this.server.close(resolve));
  }
}
