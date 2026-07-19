import dgram from "node:dgram";

const MULTICAST_ADDRESS = "224.0.0.251";
const MULTICAST_PORT = 5353;
const SERVICE_NAME = "_onegate-debug._tcp.local";

function u16(value) {
  const result = Buffer.alloc(2);
  result.writeUInt16BE(value);
  return result;
}

function u32(value) {
  const result = Buffer.alloc(4);
  result.writeUInt32BE(value);
  return result;
}

function dnsName(value) {
  const labels = value.replace(/\.$/u, "").split(".");
  const parts = [];
  for (const label of labels) {
    const bytes = Buffer.from(label, "utf8");
    if (bytes.length === 0 || bytes.length > 63) throw new Error(`Invalid DNS label: ${label}`);
    parts.push(Buffer.from([bytes.length]), bytes);
  }
  parts.push(Buffer.from([0]));
  return Buffer.concat(parts);
}

function record(name, type, data, ttl = 60) {
  return Buffer.concat([dnsName(name), u16(type), u16(1), u32(ttl), u16(data.length), data]);
}

function ipv4Bytes(address) {
  const parts = address.split(".").map((part) => Number.parseInt(part, 10));
  if (parts.length !== 4 || parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)) {
    throw new Error(`Invalid IPv4 address: ${address}`);
  }
  return Buffer.from(parts);
}

function txtData(values) {
  return Buffer.concat(values.map((value) => {
    const bytes = Buffer.from(value, "utf8");
    if (bytes.length > 255) throw new Error("mDNS TXT entries are limited to 255 bytes.");
    return Buffer.concat([Buffer.from([bytes.length]), bytes]);
  }));
}

export class RemoteDebuggerAdvertiser {
  constructor({ debuggerId, debuggerName, port, addresses }) {
    this.debuggerId = debuggerId;
    this.debuggerName = debuggerName;
    this.port = port;
    this.addresses = addresses.filter((address) => /^\d+\.\d+\.\d+\.\d+$/u.test(address));
    this.instanceName = `${debuggerId}.${SERVICE_NAME}`;
    this.hostName = `onegate-debugger-${debuggerId}.local`;
  }

  async start() {
    if (this.socket) return;
    const socket = dgram.createSocket({ type: "udp4", reuseAddr: true });
    this.socket = socket;
    socket.on("error", () => undefined);
    socket.on("message", (message) => {
      if (message.includes(Buffer.from("_onegate-debug", "utf8"))) this.announce();
    });
    await new Promise((resolve, reject) => {
      socket.once("error", reject);
      socket.bind(MULTICAST_PORT, "0.0.0.0", () => {
        socket.off("error", reject);
        try {
          socket.addMembership(MULTICAST_ADDRESS);
          socket.setMulticastTTL(255);
        } catch {
          // Pairing still works through the QR endpoints when multicast is unavailable.
        }
        resolve();
      });
    });
    this.announce();
    this.timer = setInterval(() => this.announce(), 30_000);
    this.timer.unref?.();
  }

  announce() {
    if (!this.socket) return;
    const records = [
      record(SERVICE_NAME, 12, dnsName(this.instanceName)),
      record(this.instanceName, 33, Buffer.concat([u16(0), u16(0), u16(this.port), dnsName(this.hostName)])),
      record(this.instanceName, 16, txtData([
        "v=1",
        `debuggerId=${this.debuggerId}`,
        `debuggerName=${this.debuggerName}`,
      ])),
      ...this.addresses.map((address) => record(this.hostName, 1, ipv4Bytes(address))),
    ];
    const header = Buffer.concat([
      u16(0),
      u16(0x8400),
      u16(0),
      u16(records.length),
      u16(0),
      u16(0),
    ]);
    this.socket.send(Buffer.concat([header, ...records]), MULTICAST_PORT, MULTICAST_ADDRESS, () => undefined);
  }

  async stop() {
    if (!this.socket) return;
    clearInterval(this.timer);
    const socket = this.socket;
    this.socket = undefined;
    await new Promise((resolve) => socket.close(resolve));
  }
}

export { SERVICE_NAME as ONEGATE_MDNS_SERVICE };
