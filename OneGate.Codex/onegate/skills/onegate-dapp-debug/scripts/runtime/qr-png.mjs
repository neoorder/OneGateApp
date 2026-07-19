import { deflateSync } from "node:zlib";

// Pairing invitations use a fixed QR Model 2 version 12-L symbol. It carries up
// to 367 UTF-8 bytes and avoids adding a runtime package to the portable skill.
const VERSION = 12;
const SIZE = VERSION * 4 + 17;
const DATA_CODEWORDS = 370;
const ERROR_CODEWORDS_PER_BLOCK = 24;
const BLOCK_DATA_LENGTHS = [92, 92, 93, 93];
const ALIGNMENT_CENTERS = [6, 32, 58];

const GF_EXP = new Uint8Array(512);
const GF_LOG = new Uint8Array(256);
let value = 1;
for (let index = 0; index < 255; index += 1) {
  GF_EXP[index] = value;
  GF_LOG[value] = index;
  value <<= 1;
  if (value & 0x100) value ^= 0x11d;
}
for (let index = 255; index < GF_EXP.length; index += 1) GF_EXP[index] = GF_EXP[index - 255];

function multiply(left, right) {
  if (left === 0 || right === 0) return 0;
  return GF_EXP[GF_LOG[left] + GF_LOG[right]];
}

function reedSolomonGenerator(degree) {
  let result = Uint8Array.from([1]);
  for (let index = 0; index < degree; index += 1) {
    const next = new Uint8Array(result.length + 1);
    for (let offset = 0; offset < result.length; offset += 1) {
      next[offset] ^= result[offset];
      next[offset + 1] ^= multiply(result[offset], GF_EXP[index]);
    }
    result = next;
  }
  return result;
}

const RS_GENERATOR = reedSolomonGenerator(ERROR_CODEWORDS_PER_BLOCK);

function reedSolomonRemainder(data) {
  const result = new Uint8Array(ERROR_CODEWORDS_PER_BLOCK);
  for (const byte of data) {
    const factor = byte ^ result[0];
    result.copyWithin(0, 1);
    result[result.length - 1] = 0;
    for (let index = 0; index < result.length; index += 1) {
      result[index] ^= multiply(RS_GENERATOR[index + 1], factor);
    }
  }
  return result;
}

class BitBuffer {
  constructor() {
    this.bits = [];
  }

  append(value, length) {
    for (let index = length - 1; index >= 0; index -= 1) {
      this.bits.push(((value >>> index) & 1) !== 0);
    }
  }

  appendBytes(bytes) {
    for (const byte of bytes) this.append(byte, 8);
  }
}

function encodeData(text) {
  const bytes = Buffer.from(text, "utf8");
  if (bytes.length > 367) {
    const error = new Error(`Pairing invitation is ${bytes.length} bytes; QR capacity is 367 bytes.`);
    error.code = "PAIRING_QR_TOO_LARGE";
    throw error;
  }
  const buffer = new BitBuffer();
  buffer.append(0b0100, 4);
  buffer.append(bytes.length, 16);
  buffer.appendBytes(bytes);
  const capacity = DATA_CODEWORDS * 8;
  buffer.append(0, Math.min(4, capacity - buffer.bits.length));
  while (buffer.bits.length % 8 !== 0) buffer.bits.push(false);
  let pad = 0xec;
  while (buffer.bits.length < capacity) {
    buffer.append(pad, 8);
    pad ^= 0xfd;
  }
  const data = new Uint8Array(DATA_CODEWORDS);
  for (let index = 0; index < buffer.bits.length; index += 1) {
    if (buffer.bits[index]) data[index >>> 3] |= 1 << (7 - (index & 7));
  }
  return interleaveWithErrorCorrection(data);
}

function interleaveWithErrorCorrection(data) {
  const blocks = [];
  let offset = 0;
  for (const length of BLOCK_DATA_LENGTHS) {
    const blockData = data.slice(offset, offset + length);
    blocks.push({ data: blockData, error: reedSolomonRemainder(blockData) });
    offset += length;
  }
  const result = [];
  for (let index = 0; index < Math.max(...BLOCK_DATA_LENGTHS); index += 1) {
    for (const block of blocks) if (index < block.data.length) result.push(block.data[index]);
  }
  for (let index = 0; index < ERROR_CODEWORDS_PER_BLOCK; index += 1) {
    for (const block of blocks) result.push(block.error[index]);
  }
  return Uint8Array.from(result);
}

function createBaseMatrix() {
  const modules = Array.from({ length: SIZE }, () => Array(SIZE).fill(false));
  const functions = Array.from({ length: SIZE }, () => Array(SIZE).fill(false));
  const setFunction = (x, y, dark) => {
    if (x < 0 || y < 0 || x >= SIZE || y >= SIZE) return;
    modules[y][x] = dark;
    functions[y][x] = true;
  };
  const drawFinder = (centerX, centerY) => {
    for (let dy = -4; dy <= 4; dy += 1) {
      for (let dx = -4; dx <= 4; dx += 1) {
        const distance = Math.max(Math.abs(dx), Math.abs(dy));
        setFunction(centerX + dx, centerY + dy, distance !== 2 && distance !== 4);
      }
    }
  };
  drawFinder(3, 3);
  drawFinder(SIZE - 4, 3);
  drawFinder(3, SIZE - 4);

  for (let index = 8; index < SIZE - 8; index += 1) {
    setFunction(6, index, index % 2 === 0);
    setFunction(index, 6, index % 2 === 0);
  }
  for (let centerYIndex = 0; centerYIndex < ALIGNMENT_CENTERS.length; centerYIndex += 1) {
    for (let centerXIndex = 0; centerXIndex < ALIGNMENT_CENTERS.length; centerXIndex += 1) {
      const overlapsFinder = (centerXIndex === 0 && centerYIndex === 0)
        || (centerXIndex === ALIGNMENT_CENTERS.length - 1 && centerYIndex === 0)
        || (centerXIndex === 0 && centerYIndex === ALIGNMENT_CENTERS.length - 1);
      if (overlapsFinder) continue;
      const centerX = ALIGNMENT_CENTERS[centerXIndex];
      const centerY = ALIGNMENT_CENTERS[centerYIndex];
      for (let dy = -2; dy <= 2; dy += 1) {
        for (let dx = -2; dx <= 2; dx += 1) {
          setFunction(centerX + dx, centerY + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
        }
      }
    }
  }
  drawFormatBits(modules, functions, 0, true);
  drawVersionBits(modules, functions);
  return { modules, functions };
}

function bchRemainder(value, polynomial) {
  let result = value;
  const polynomialDegree = 31 - Math.clz32(polynomial);
  while (result !== 0 && 31 - Math.clz32(result) >= polynomialDegree) {
    result ^= polynomial << ((31 - Math.clz32(result)) - polynomialDegree);
  }
  return result;
}

function drawVersionBits(modules, functions) {
  const bits = (VERSION << 12) | bchRemainder(VERSION << 12, 0x1f25);
  for (let index = 0; index < 18; index += 1) {
    const dark = ((bits >>> index) & 1) !== 0;
    const x = SIZE - 11 + (index % 3);
    const y = Math.floor(index / 3);
    modules[y][x] = dark;
    functions[y][x] = true;
    modules[x][y] = dark;
    functions[x][y] = true;
  }
}

function drawFormatBits(modules, functions, mask, reserveOnly = false) {
  const data = (0b01 << 3) | mask; // Error correction level L.
  const bits = ((data << 10) | bchRemainder(data << 10, 0x537)) ^ 0x5412;
  const set = (x, y, bit) => {
    modules[y][x] = reserveOnly ? false : bit;
    functions[y][x] = true;
  };
  for (let index = 0; index <= 5; index += 1) set(8, index, ((bits >>> index) & 1) !== 0);
  set(8, 7, ((bits >>> 6) & 1) !== 0);
  set(8, 8, ((bits >>> 7) & 1) !== 0);
  set(7, 8, ((bits >>> 8) & 1) !== 0);
  for (let index = 9; index < 15; index += 1) set(14 - index, 8, ((bits >>> index) & 1) !== 0);
  for (let index = 0; index < 8; index += 1) set(SIZE - 1 - index, 8, ((bits >>> index) & 1) !== 0);
  for (let index = 8; index < 15; index += 1) set(8, SIZE - 15 + index, ((bits >>> index) & 1) !== 0);
  set(8, SIZE - 8, true);
}

function maskBit(mask, x, y) {
  switch (mask) {
    case 0: return (x + y) % 2 === 0;
    case 1: return y % 2 === 0;
    case 2: return x % 3 === 0;
    case 3: return (x + y) % 3 === 0;
    case 4: return (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0;
    case 5: return (x * y) % 2 + (x * y) % 3 === 0;
    case 6: return ((x * y) % 2 + (x * y) % 3) % 2 === 0;
    case 7: return ((x + y) % 2 + (x * y) % 3) % 2 === 0;
    default: throw new Error(`Invalid QR mask: ${mask}`);
  }
}

function placeCodewords(base, codewords, mask) {
  const modules = base.modules.map((row) => row.slice());
  let bitIndex = 0;
  let upward = true;
  for (let right = SIZE - 1; right >= 1; right -= 2) {
    if (right === 6) right -= 1;
    for (let vertical = 0; vertical < SIZE; vertical += 1) {
      const y = upward ? SIZE - 1 - vertical : vertical;
      for (let column = 0; column < 2; column += 1) {
        const x = right - column;
        if (base.functions[y][x]) continue;
        const dataBit = bitIndex < codewords.length * 8
          && ((codewords[bitIndex >>> 3] >>> (7 - (bitIndex & 7))) & 1) !== 0;
        modules[y][x] = dataBit !== maskBit(mask, x, y);
        bitIndex += 1;
      }
    }
    upward = !upward;
  }
  drawFormatBits(modules, base.functions, mask);
  return modules;
}

function penaltyScore(modules) {
  let penalty = 0;
  for (let axis = 0; axis < 2; axis += 1) {
    for (let outer = 0; outer < SIZE; outer += 1) {
      let runColor = false;
      let runLength = 0;
      for (let inner = 0; inner < SIZE; inner += 1) {
        const color = axis === 0 ? modules[outer][inner] : modules[inner][outer];
        if (inner === 0 || color !== runColor) {
          runColor = color;
          runLength = 1;
        } else {
          runLength += 1;
          if (runLength === 5) penalty += 3;
          else if (runLength > 5) penalty += 1;
        }
      }
    }
  }
  for (let y = 0; y < SIZE - 1; y += 1) {
    for (let x = 0; x < SIZE - 1; x += 1) {
      const color = modules[y][x];
      if (modules[y][x + 1] === color && modules[y + 1][x] === color && modules[y + 1][x + 1] === color) {
        penalty += 3;
      }
    }
  }
  let dark = 0;
  for (const row of modules) for (const module of row) if (module) dark += 1;
  penalty += Math.floor(Math.abs(dark * 20 - SIZE * SIZE * 10) / (SIZE * SIZE)) * 10;
  return penalty;
}

export function encodeQrMatrix(text) {
  const codewords = encodeData(text);
  const base = createBaseMatrix();
  let best;
  let bestPenalty = Number.POSITIVE_INFINITY;
  for (let mask = 0; mask < 8; mask += 1) {
    const modules = placeCodewords(base, codewords, mask);
    const penalty = penaltyScore(modules);
    if (penalty < bestPenalty) {
      best = modules;
      bestPenalty = penalty;
    }
  }
  return best;
}

const CRC_TABLE = new Uint32Array(256);
for (let index = 0; index < 256; index += 1) {
  let crc = index;
  for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  CRC_TABLE[index] = crc >>> 0;
}

function crc32(value) {
  let crc = 0xffffffff;
  for (const byte of value) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function pngChunk(type, data) {
  const typeBytes = Buffer.from(type, "ascii");
  const body = Buffer.concat([typeBytes, data]);
  const header = Buffer.alloc(4);
  header.writeUInt32BE(data.length);
  const footer = Buffer.alloc(4);
  footer.writeUInt32BE(crc32(body));
  return Buffer.concat([header, body, footer]);
}

export function renderQrPng(text, { scale = 6, border = 4 } = {}) {
  if (!Number.isInteger(scale) || scale < 1 || scale > 32) throw new RangeError("QR scale must be 1..32.");
  const modules = encodeQrMatrix(text);
  const width = (SIZE + border * 2) * scale;
  const pixels = Buffer.alloc((width + 1) * width, 0xff);
  for (let y = 0; y < width; y += 1) {
    const row = y * (width + 1);
    pixels[row] = 0;
    for (let x = 0; x < width; x += 1) {
      const moduleX = Math.floor(x / scale) - border;
      const moduleY = Math.floor(y / scale) - border;
      const dark = moduleX >= 0 && moduleY >= 0 && moduleX < SIZE && moduleY < SIZE
        && modules[moduleY][moduleX];
      pixels[row + x + 1] = dark ? 0x00 : 0xff;
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(width, 4);
  ihdr[8] = 8;
  ihdr[9] = 0;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", deflateSync(pixels, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}
