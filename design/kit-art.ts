#!/usr/bin/env node
// Derives the Desktop Kit's plate sprites from one material image, and cuts its state marks
// from one sheet of painted icons
// (design/components/InGameKit/README.md, "The plate art").
//
//   bun design/kit-art.ts          write the plate PNGs into the mod's Resources/DesktopKit/
//   bun design/kit-art.ts --check  fail if a plate PNG differs from what this script draws
//                                  (`bun design/build.ts --check` runs the same comparison)
//
// Every plate shares one rounded silhouette with a soft painted rim. A state is the same
// material graded toward a colour. Sprites are drawn at 4x and reduced, and colour is bled
// under the transparent pixels so bilinear filtering leaves no dark fringe.
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import zlib from 'node:zlib';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const materialPath = path.join(
  root,
  'design/components/InGameKit/art/material.png'
);
const marksPath = path.join(root, 'design/components/InGameKit/art/marks.png');
const outDir = path.join(
  root,
  'bazaarplusplus-mod/src/BazaarPlusPlus/Resources/DesktopKit'
);

/** Supersampling: sprites are drawn at this multiple and reduced. */
const SS = 4;
/** Sprite pixels per canvas unit; DesktopNativeSkin.KitPixelsPerUnit. */
const PIXELS_PER_UNIT = 3;
/** The silhouette's corner radius in canvas units; DesktopNativeSkin.KitBorderPixels is (radius + 4) x 3. */
const RADIUS = 6;
/** Transparent room around the silhouette for its soft edge, in canvas units. */
const PAD = 0.5;
/** One canvas unit in supersampled pixels. */
const UNIT = PIXELS_PER_UNIT * SS;

type Rgb = readonly [number, number, number];

/** A sample of a typed array; out of range reads 0. */
const at = (samples: ArrayLike<number>, index: number) => samples[index] ?? 0;

interface Raster {
  width: number;
  height: number;
  /** RGBA, 0..1, not premultiplied. */
  data: Float32Array;
}

interface PlateSpec {
  fill: string;
  rim: string;
  rimAlpha: number;
  /** Light from above: the top is lifted and the bottom weighted by this fraction. */
  lift?: number;
  /** How strongly the material's grain shows. */
  contrast?: number;
  radius?: number;
  widthUnits?: number;
  heightUnits?: number;
}

const rim = '#946e33';
const gold = '#dba84c';
const plates: Record<string, PlateSpec> = {
  plate: { fill: '#3a2815', rim, rimAlpha: 0.62 },
  'plate-hover': { fill: '#553b1b', rim: gold, rimAlpha: 0.62, contrast: 0.07 },
  'plate-pressed': { fill: '#24180c', rim, rimAlpha: 0.42, lift: -0.06 },
  'plate-primary': {
    fill: '#6b4718',
    rim: '#ffc759',
    rimAlpha: 0.62,
    contrast: 0.08
  },
  'plate-primary-hover': {
    fill: '#8c6124',
    rim: '#ffc759',
    rimAlpha: 0.75,
    contrast: 0.08
  },
  'plate-danger': { fill: '#8f2f22', rim: '#e6664f', rimAlpha: 0.55 },
  'plate-danger-hover': { fill: '#ad3b2b', rim: '#f08a72', rimAlpha: 0.65 },
  'plate-danger-pressed': {
    fill: '#6f2218',
    rim: '#e6664f',
    rimAlpha: 0.4,
    lift: -0.06
  },
  ground: {
    fill: '#21180f',
    rim,
    rimAlpha: 0.5,
    lift: 0.03,
    contrast: 0.06,
    radius: RADIUS + 2,
    heightUnits: 64
  }
};

function rgb(hex: string): Rgb {
  const channel = (from: number) =>
    Number.parseInt(hex.replace('#', '').slice(from, from + 2), 16) / 255;
  return [channel(0), channel(2), channel(4)];
}

const clamp = (value: number) => Math.min(1, Math.max(0, value));

/** Signed distance to the padded rounded rectangle, in supersampled pixels (negative inside). */
function distance(
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number
): number {
  const qx = Math.abs(x + 0.5 - width / 2) - (width / 2 - radius);
  const qy = Math.abs(y + 0.5 - height / 2) - (height / 2 - radius);
  const outside = Math.hypot(Math.max(qx, 0), Math.max(qy, 0));
  return outside + Math.min(Math.max(qx, qy), 0) - radius + PAD * UNIT;
}

const coverage = (d: number) => clamp(0.5 - d / (1.2 * SS));
const bell = (d: number, centre: number, width: number) =>
  Math.exp(-(((d - centre) / width) ** 2));

function size(widthUnits = 64, heightUnits = 36) {
  return { width: widthUnits * UNIT, height: heightUnits * UNIT };
}

/** The material's grain over a sprite, as deviations from its mean in standard deviations. */
function grain(material: Gray, width: number, height: number): Float32Array {
  const out = new Float32Array(width * height);
  let sum = 0;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const value = sample(material, x / SS, y / SS);
      out[y * width + x] = value;
      sum += value;
    }
  }
  const mean = sum / out.length;
  let spread = 0;
  for (const value of out) spread += (value - mean) ** 2;
  const deviation = Math.sqrt(spread / out.length) + 1e-6;
  for (let i = 0; i < out.length; i++) out[i] = (at(out, i) - mean) / deviation;
  return out;
}

function plate(material: Gray, spec: PlateSpec): Raster {
  const { width, height } = size(spec.widthUnits, spec.heightUnits);
  const radius = (spec.radius ?? RADIUS) * UNIT;
  const lift = spec.lift ?? 0.1;
  const contrast = spec.contrast ?? 0.1;
  const fill = rgb(spec.fill);
  const rimColour = rgb(spec.rim);
  const texture = grain(material, width, height);
  const data = new Float32Array(width * height * 4);
  for (let y = 0; y < height; y++) {
    const down = y / (height - 1);
    const ramp = 1 + lift - 2 * lift * down;
    for (let x = 0; x < width; x++) {
      const d = distance(x, y, width, height, radius);
      // The painted rim: a soft band just inside the edge, strongest at the top.
      const band =
        bell(d, -1.6 * UNIT, 0.9 * UNIT) * (1 - 0.45 * down) * spec.rimAlpha;
      // A faint dark seat at the very edge, so the plate sits on any ground.
      const seat = 1 - bell(d, -0.3 * UNIT, 0.5 * UNIT) * 0.35;
      const to = (y * width + x) * 4;
      const lit = 1 + contrast * at(texture, y * width + x);
      for (let channel = 0; channel < 3; channel++) {
        const base = clamp(clamp(fill[channel] * lit) * ramp);
        data[to + channel] =
          (base * (1 - band) + rimColour[channel] * band) * seat;
      }
      data[to + 3] = coverage(d);
    }
  }
  return reduce({ width, height, data });
}

/** A line with no centre that follows the plates' silhouette: a core and a wider, fainter halo. */
function edge(colour: string, peak: number, widthUnits: number): Raster {
  const { width, height } = size();
  const tint = rgb(colour);
  const data = new Float32Array(width * height * 4);
  for (let y = 0; y < height; y++) {
    const fade = 1 - 0.3 * (y / (height - 1));
    for (let x = 0; x < width; x++) {
      const d = distance(x, y, width, height, RADIUS * UNIT);
      const line =
        bell(d, -0.9 * UNIT, widthUnits * UNIT) +
        bell(d, -0.9 * UNIT, 2.6 * widthUnits * UNIT) * 0.35;
      const to = (y * width + x) * 4;
      data.set(tint, to);
      data[to + 3] = clamp(line * fade * peak) * coverage(d);
    }
  }
  return reduce({ width, height, data });
}

/** The silhouette in solid white. */
function shape(): Raster {
  const { width, height } = size();
  const data = new Float32Array(width * height * 4).fill(1);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      data[(y * width + x) * 4 + 3] = coverage(
        distance(x, y, width, height, RADIUS * UNIT)
      );
    }
  }
  return reduce({ width, height, data });
}

/** Reduces a supersampled raster with premultiplied alpha, then bleeds colour outwards. */
function reduce(source: Raster, factor = SS): Raster {
  const width = source.width / factor;
  const height = source.height / factor;
  const data = new Float32Array(width * height * 4);
  const known = new Uint8Array(width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const sum = new Float64Array(4);
      for (let dy = 0; dy < factor; dy++) {
        for (let dx = 0; dx < factor; dx++) {
          const from = ((y * factor + dy) * source.width + x * factor + dx) * 4;
          const alpha = at(source.data, from + 3);
          for (let channel = 0; channel < 3; channel++) {
            sum[channel] =
              at(sum, channel) + at(source.data, from + channel) * alpha;
          }
          sum[3] = at(sum, 3) + alpha;
        }
      }
      const to = (y * width + x) * 4;
      const total = at(sum, 3);
      const alpha = total / (factor * factor);
      if (total > 1e-4) {
        for (let channel = 0; channel < 3; channel++)
          data[to + channel] = at(sum, channel) / total;
        known[y * width + x] = alpha > 0.02 ? 1 : 0;
      }
      data[to + 3] = alpha;
    }
  }
  bleed(data, known, width, height);
  return { width, height, data };
}

/** Gives each transparent pixel the mean colour of its coloured neighbours, a ring at a time. */
function bleed(
  data: Float32Array,
  known: Uint8Array,
  width: number,
  height: number
) {
  for (let pass = 0; pass < 12; pass++) {
    const next = known.slice();
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        if (known[y * width + x]) continue;
        const sum = new Float64Array(3);
        let count = 0;
        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            const nx = x + dx;
            const ny = y + dy;
            if (
              nx < 0 ||
              ny < 0 ||
              nx >= width ||
              ny >= height ||
              !known[ny * width + nx]
            )
              continue;
            for (let channel = 0; channel < 3; channel++) {
              sum[channel] =
                at(sum, channel) + at(data, (ny * width + nx) * 4 + channel);
            }
            count++;
          }
        }
        if (count === 0) continue;
        for (let channel = 0; channel < 3; channel++) {
          data[(y * width + x) * 4 + channel] = at(sum, channel) / count;
        }
        next[y * width + x] = 1;
      }
    }
    known.set(next);
  }
}

// ---- PNG: 8-bit, non-interlaced; grey in, RGBA out ----

interface Gray {
  width: number;
  height: number;
  data: Uint8Array;
}

const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function chunks(file: Buffer): Map<string, Buffer[]> {
  if (!file.subarray(0, 8).equals(signature))
    throw new Error('Not a PNG file.');
  const found = new Map<string, Buffer[]>();
  for (let offset = 8; offset < file.length;) {
    const length = file.readUInt32BE(offset);
    const type = file.toString('latin1', offset + 4, offset + 8);
    found.set(type, [
      ...(found.get(type) ?? []),
      file.subarray(offset + 8, offset + 8 + length)
    ]);
    offset += 12 + length;
  }
  return found;
}

/** Decodes an 8-bit non-interlaced PNG to its raw samples. */
function decode(file: Buffer): {
  width: number;
  height: number;
  channels: number;
  data: Uint8Array;
} {
  const found = chunks(file);
  const header = found.get('IHDR')?.[0];
  const channels = new Map([
    [0, 1],
    [2, 3],
    [4, 2],
    [6, 4]
  ]).get(header?.[9] ?? -1);
  if (
    header === undefined ||
    header[8] !== 8 ||
    channels === undefined ||
    header[12] !== 0
  ) {
    throw new Error(
      'Only 8-bit non-interlaced grey, RGB or RGBA PNGs are supported.'
    );
  }
  const width = header.readUInt32BE(0);
  const height = header.readUInt32BE(4);
  const raw = zlib.inflateSync(Buffer.concat(found.get('IDAT') ?? []));
  const stride = width * channels;
  const data = new Uint8Array(stride * height);
  for (let y = 0; y < height; y++) {
    const filter = at(raw, y * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? at(data, y * stride + x - channels) : 0;
      const up = y > 0 ? at(data, (y - 1) * stride + x) : 0;
      const corner =
        x >= channels && y > 0 ? at(data, (y - 1) * stride + x - channels) : 0;
      const paeth = () => {
        const estimate = left + up - corner;
        const [a, b, c] = [
          Math.abs(estimate - left),
          Math.abs(estimate - up),
          Math.abs(estimate - corner)
        ];
        return a <= b && a <= c ? left : b <= c ? up : corner;
      };
      const predicted = [0, left, up, (left + up) >> 1, paeth()][filter];
      if (predicted === undefined)
        throw new Error(`Unknown PNG filter ${filter}.`);
      data[y * stride + x] =
        (at(raw, y * (stride + 1) + 1 + x) + predicted) & 0xff;
    }
  }
  return { width, height, channels, data };
}

function readMaterial(): Gray {
  const image = decode(fs.readFileSync(materialPath));
  if (image.channels !== 1)
    throw new Error('The material must be a greyscale PNG.');
  return image;
}

/** Bilinear sample of the material, 0..1, wrapping at its edges. */
function sample(material: Gray, x: number, y: number): number {
  const x0 = Math.floor(x);
  const y0 = Math.floor(y);
  const fx = x - x0;
  const fy = y - y0;
  const grey = (px: number, py: number) =>
    at(
      material.data,
      (py % material.height) * material.width + (px % material.width)
    ) / 255;
  return (
    (grey(x0, y0) * (1 - fx) + grey(x0 + 1, y0) * fx) * (1 - fy) +
    (grey(x0, y0 + 1) * (1 - fx) + grey(x0 + 1, y0 + 1) * fx) * fy
  );
}

function bytes(raster: Raster): Uint8Array {
  return Uint8Array.from(raster.data, (value) =>
    Math.round(clamp(value) * 255)
  );
}

const crcTable = Uint32Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit++)
    value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  return value >>> 0;
});

function crc(buffer: Buffer): number {
  let value = 0xffffffff;
  for (const byte of buffer)
    value = at(crcTable, (value ^ byte) & 0xff) ^ (value >>> 8);
  return (value ^ 0xffffffff) >>> 0;
}

function chunk(type: string, body: Buffer): Buffer {
  const head = Buffer.alloc(4);
  head.writeUInt32BE(body.length);
  const tagged = Buffer.concat([Buffer.from(type, 'latin1'), body]);
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc(tagged));
  return Buffer.concat([head, tagged, tail]);
}

function encode(raster: Raster): Buffer {
  const pixels = bytes(raster);
  const stride = raster.width * 4;
  const raw = Buffer.alloc((stride + 1) * raster.height);
  for (let y = 0; y < raster.height; y++) {
    raw.set(
      pixels.subarray(y * stride, (y + 1) * stride),
      y * (stride + 1) + 1
    );
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(raster.width, 0);
  header.writeUInt32BE(raster.height, 4);
  header.set([8, 6, 0, 0, 0], 8);
  return Buffer.concat([
    signature,
    chunk('IHDR', header),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0))
  ]);
}

// ---- entry ----

/**
 * The state marks, cut from the sheet of painted icons: one square cell per mark, in this order.
 * A mark is half its cell's size, dense enough for a phone.
 */
const markNames = ['completed', 'abandoned', 'active', 'won', 'lost'];
const MARK_REDUCTION = 2;

/**
 * Grades a mark to dull iron. The completed mark is a cup, and in a bright metal it reads
 * as one of the game's trophies beside it in a list; a run below the first tier has no metal.
 */
function iron(data: Float32Array): void {
  for (let i = 0; i < data.length; i += 4) {
    const luma =
      0.299 * at(data, i) + 0.587 * at(data, i + 1) + 0.114 * at(data, i + 2);
    data[i] = luma * 0.62;
    data[i + 1] = luma * 0.55;
    data[i + 2] = luma * 0.5;
  }
}

function marks(): Map<string, Raster> {
  const sheet = decode(fs.readFileSync(marksPath));
  const cell = sheet.height;
  if (sheet.channels !== 4 || sheet.width !== cell * markNames.length) {
    throw new Error(
      `The marks sheet must be RGBA, ${markNames.length} square cells wide.`
    );
  }
  const cut = new Map<string, Raster>();
  markNames.forEach((name, index) => {
    const data = new Float32Array(cell * cell * 4);
    for (let y = 0; y < cell; y++) {
      for (let x = 0; x < cell * 4; x++) {
        data[y * cell * 4 + x] =
          at(sheet.data, (y * sheet.width + index * cell) * 4 + x) / 255;
      }
    }
    if (name === 'completed') iron(data);
    cut.set(name, reduce({ width: cell, height: cell, data }, MARK_REDUCTION));
  });
  return cut;
}

/**
 * The supporter heart in solid white, tinted where it is drawn: two round lobes over a
 * point, from the implicit heart curve (x^2 + y^2 - 1)^3 = x^2 y^3.
 */
function heart(): Raster {
  const side = 16 * UNIT;
  const data = new Float32Array(side * side * 4).fill(1);
  const curve = (px: number, py: number) => {
    const x = ((px + 0.5) / side - 0.5) * 2.6;
    const y = (0.5 - (py + 0.5) / side) * 2.6 + 0.18;
    return (x * x + y * y - 1) ** 3 - x * x * y * y * y;
  };
  for (let y = 0; y < side; y++) {
    for (let x = 0; x < side; x++) {
      // The curve's value over its gradient is the distance to the outline in pixels.
      const value = curve(x, y);
      const slope = Math.hypot(
        curve(x + 1, y) - curve(x - 1, y),
        curve(x, y + 1) - curve(x, y - 1)
      );
      data[(y * side + x) * 4 + 3] = coverage((2 * value) / (slope + 1e-9));
    }
  }
  return reduce({ width: side, height: side, data });
}

function draw(): Map<string, Raster> {
  const material = readMaterial();
  const sprites = new Map<string, Raster>([['shape', shape()]]);
  for (const [name, spec] of Object.entries(plates))
    sprites.set(name, plate(material, spec));
  sprites.set('edge', edge(gold, 0.85, 0.42));
  sprites.set('edge-bright', edge('#ffc759', 1, 0.5));
  for (const [name, raster] of marks()) sprites.set(`mark-${name}`, raster);
  sprites.set('icon-heart', heart());
  return sprites;
}

/** A committed plate, or undefined when it is missing or not a PNG this script can read. */
function readPlate(file: string) {
  try {
    return decode(fs.readFileSync(file));
  } catch {
    return undefined;
  }
}

/** The plate PNGs that differ from what this script draws; compared by pixel, not by file bytes. */
export function stalePlates(): string[] {
  const stale: string[] = [];
  for (const [name, raster] of draw()) {
    const file = path.join(outDir, `${name}.png`);
    const committed = readPlate(file);
    const fresh = bytes(raster);
    if (
      committed?.width !== raster.width ||
      committed.height !== raster.height ||
      committed.channels !== 4 ||
      // One level of slack: Math.exp and Math.hypot may round differently on another platform.
      !committed.data.every(
        (value, index) => Math.abs(value - at(fresh, index)) <= 1
      )
    ) {
      stale.push(path.relative(root, file));
    }
  }
  return stale;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  if (process.argv.includes('--check')) {
    const stale = stalePlates();
    if (stale.length > 0) {
      console.error(
        `Stale Desktop Kit plates; run \`just design::kit-art\`:\n  ${stale.join('\n  ')}`
      );
      process.exit(1);
    }
    console.log('Desktop Kit plates match design/kit-art.ts.');
  } else {
    fs.mkdirSync(outDir, { recursive: true });
    for (const [name, raster] of draw())
      fs.writeFileSync(path.join(outDir, `${name}.png`), encode(raster));
    console.log(
      `Wrote the Desktop Kit plates to ${path.relative(root, outDir)}.`
    );
  }
}
