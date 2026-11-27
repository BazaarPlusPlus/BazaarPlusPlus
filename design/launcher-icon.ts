/**
 * Export the desktop app icon as the Android App's adaptive launcher icon.
 *
 * The desktop icon (bazaarplusplus-installer/src-tauri/icons/app-icon.png) is
 * artwork on a dark squircle whose fill is a vertical gradient. An adaptive
 * icon needs two layers that the launcher masks itself, so:
 *
 * - background: the squircle's gradient, extended over the whole 108dp layer;
 * - foreground: the icon with the squircle's rim feathered away, scaled so the
 *   artwork fits the 66dp safe zone every launcher mask keeps.
 *
 * Both are written for every density as res/mipmap-*\/ic_launcher_{background,foreground}.png,
 * and res/mipmap-anydpi/ic_launcher.xml records the source's sha256, which
 * `just design::check` compares with the desktop icon to catch a stale export.
 */
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const REPO_ROOT = path.resolve(import.meta.dirname, '..');
const DEFAULT_SOURCE = path.join(
  REPO_ROOT,
  'bazaarplusplus-installer/src-tauri/icons/app-icon.png'
);
const DEFAULT_RES = path.join(
  REPO_ROOT,
  'bazaarplusplus-android/app/src/main/res'
);

const DENSITIES: readonly (readonly [string, number])[] = [
  ['mdpi', 1],
  ['hdpi', 1.5],
  ['xhdpi', 2],
  ['xxhdpi', 3],
  ['xxxhdpi', 4]
];
const LAYER_DP = 108;
const SAFE_RADIUS_DP = 33;
// Rows and columns of the source that are squircle fill, not rim or artwork,
// for fitting the gradient: the left margin between the rim and the artwork.
const FILL_X_START = 60;
const FILL_X_END = 90;
const FILL_Y_START = 220;
const FILL_Y_END = 820;

const PNG_SIGNATURE = Buffer.from([
  0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
]);

export interface RgbaImage {
  width: number;
  height: number;
  data: Uint8Array;
}

const crcTable = Uint32Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit++)
    value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  return value >>> 0;
});

function crc32(buffer: Buffer): number {
  let value = 0xffffffff;
  for (const byte of buffer) {
    value = (crcTable[(value ^ byte) & 0xff] ?? 0) ^ (value >>> 8);
  }
  return (value ^ 0xffffffff) >>> 0;
}

function pngChunk(type: string, body: Buffer): Buffer {
  const head = Buffer.alloc(4);
  head.writeUInt32BE(body.length, 0);
  const tagged = Buffer.concat([Buffer.from(type, 'latin1'), body]);
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc32(tagged), 0);
  return Buffer.concat([head, tagged, tail]);
}

function paethPredictor(a: number, b: number, c: number): number {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}

/** Decode an 8-bit or 16-bit non-interlaced grey/RGB/RGBA PNG into 8-bit RGBA. */
export function decodePngToRgba(file: Buffer): RgbaImage {
  if (!file.subarray(0, 8).equals(PNG_SIGNATURE)) {
    throw new Error('Not a PNG file.');
  }
  let header: Buffer | undefined;
  const idat: Buffer[] = [];
  for (let offset = 8; offset < file.length;) {
    const length = file.readUInt32BE(offset);
    const type = file.toString('latin1', offset + 4, offset + 8);
    const body = file.subarray(offset + 8, offset + 8 + length);
    if (type === 'IHDR') header = body;
    else if (type === 'IDAT') idat.push(body);
    else if (type === 'IEND') break;
    offset += 12 + length;
  }
  if (!header || header.length < 13) {
    throw new Error('PNG is missing a valid IHDR chunk.');
  }
  const width = header.readUInt32BE(0);
  const height = header.readUInt32BE(4);
  const bitDepth = header[8];
  const colorType = header[9];
  const interlace = header[12];
  const channels =
    colorType === 0
      ? 1
      : colorType === 2
        ? 3
        : colorType === 4
          ? 2
          : colorType === 6
            ? 4
            : 0;
  if (
    (bitDepth !== 8 && bitDepth !== 16) ||
    channels === 0 ||
    interlace !== 0
  ) {
    throw new Error(
      `Unsupported PNG format (bitDepth=${bitDepth}, colorType=${colorType}, interlace=${interlace}).`
    );
  }

  const raw = zlib.inflateSync(Buffer.concat(idat));
  const sampleBytes = bitDepth / 8;
  const bpp = channels * sampleBytes;
  const stride = width * bpp;
  const scanlines = new Uint8Array(stride * height);

  for (let y = 0; y < height; y++) {
    const rowStart = y * (stride + 1);
    const filter = raw[rowStart];
    const outRow = y * stride;
    const prevRow = (y - 1) * stride;
    for (let x = 0; x < stride; x++) {
      const left = x >= bpp ? scanlines[outRow + x - bpp] : 0;
      const up = y > 0 ? scanlines[prevRow + x] : 0;
      const corner = x >= bpp && y > 0 ? scanlines[prevRow + x - bpp] : 0;
      let predicted: number;
      if (filter === 0) predicted = 0;
      else if (filter === 1) predicted = left;
      else if (filter === 2) predicted = up;
      else if (filter === 3) predicted = (left + up) >> 1;
      else if (filter === 4) predicted = paethPredictor(left, up, corner);
      else throw new Error(`Unknown PNG filter ${filter}.`);
      scanlines[outRow + x] = (raw[rowStart + 1 + x] + predicted) & 0xff;
    }
  }

  const readSample = (offset: number): number =>
    sampleBytes === 1
      ? scanlines[offset]
      : Math.round(((scanlines[offset] << 8) | scanlines[offset + 1]) / 257);

  const rgba = new Uint8Array(width * height * 4);
  for (let index = 0; index < width * height; index++) {
    const src = index * bpp;
    const dst = index * 4;
    if (channels === 1) {
      const g = readSample(src);
      rgba[dst] = g;
      rgba[dst + 1] = g;
      rgba[dst + 2] = g;
      rgba[dst + 3] = 255;
    } else if (channels === 2) {
      const g = readSample(src);
      rgba[dst] = g;
      rgba[dst + 1] = g;
      rgba[dst + 2] = g;
      rgba[dst + 3] = readSample(src + sampleBytes);
    } else if (channels === 3) {
      rgba[dst] = readSample(src);
      rgba[dst + 1] = readSample(src + sampleBytes);
      rgba[dst + 2] = readSample(src + 2 * sampleBytes);
      rgba[dst + 3] = 255;
    } else {
      rgba[dst] = readSample(src);
      rgba[dst + 1] = readSample(src + sampleBytes);
      rgba[dst + 2] = readSample(src + 2 * sampleBytes);
      rgba[dst + 3] = readSample(src + 3 * sampleBytes);
    }
  }
  return { width, height, data: rgba };
}

/** Encode an 8-bit RGB (3 channels) or RGBA (4 channels) image as PNG. */
export function encodePng(
  width: number,
  height: number,
  data: Uint8Array,
  channels: 3 | 4
): Buffer {
  const stride = width * channels;
  if (data.length !== stride * height) {
    throw new Error(
      `Expected ${stride * height} bytes for ${width}x${height}x${channels}, got ${data.length}.`
    );
  }
  const raw = Buffer.alloc((stride + 1) * height);
  // Use Sub filter (1) on each row for tighter deflate compression.
  for (let y = 0; y < height; y++) {
    const srcRow = y * stride;
    const dstRow = y * (stride + 1);
    raw[dstRow] = 1;
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? data[srcRow + x - channels] : 0;
      raw[dstRow + 1 + x] = (data[srcRow + x] - left) & 0xff;
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = channels === 4 ? 6 : 2;
  header[10] = 0;
  header[11] = 0;
  header[12] = 0;
  const compressed = zlib.deflateSync(raw, { level: 9 });
  return Buffer.concat([
    PNG_SIGNATURE,
    pngChunk('IHDR', header),
    pngChunk('IDAT', compressed),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
}

function clampByte(value: number): number {
  return value <= 0 ? 0 : value >= 255 ? 255 : Math.round(value);
}

function roundedRectMask(
  size: number,
  inset: number,
  radius: number
): Float32Array {
  const mask = new Float32Array(size * size);
  const min = inset;
  const max = size - inset;
  const innerMin = min + radius;
  const innerMax = max - radius;
  const r2 = radius * radius;
  for (let y = 0; y < size; y++) {
    const py = y + 0.5;
    const dy =
      py < innerMin ? innerMin - py : py > innerMax ? py - innerMax : 0;
    const dy2 = dy * dy;
    const row = y * size;
    for (let x = 0; x < size; x++) {
      const px = x + 0.5;
      if (px < min || px > max || py < min || py > max) continue;
      const dx =
        px < innerMin ? innerMin - px : px > innerMax ? px - innerMax : 0;
      if (dx * dx + dy2 <= r2) mask[row + x] = 1;
    }
  }
  return mask;
}

function gaussianBlur(
  input: Float32Array,
  width: number,
  height: number,
  sigma: number
): Float32Array {
  if (sigma <= 0) return input.slice();
  const radius = Math.max(1, Math.ceil(sigma * 3));
  const kernel = new Float32Array(radius * 2 + 1);
  let sum = 0;
  const inv2Sigma2 = 1 / (2 * sigma * sigma);
  for (let i = -radius; i <= radius; i++) {
    const weight = Math.exp(-(i * i) * inv2Sigma2);
    kernel[i + radius] = weight;
    sum += weight;
  }
  for (let i = 0; i < kernel.length; i++) kernel[i] /= sum;

  const temp = new Float32Array(width * height);
  for (let y = 0; y < height; y++) {
    const row = y * width;
    for (let x = 0; x < width; x++) {
      let acc = 0;
      for (let k = -radius; k <= radius; k++) {
        const sx = Math.min(width - 1, Math.max(0, x + k));
        acc += input[row + sx] * kernel[k + radius];
      }
      temp[row + x] = acc;
    }
  }

  const output = new Float32Array(width * height);
  for (let y = 0; y < height; y++) {
    const row = y * width;
    for (let x = 0; x < width; x++) {
      let acc = 0;
      for (let k = -radius; k <= radius; k++) {
        const sy = Math.min(height - 1, Math.max(0, y + k));
        acc += temp[sy * width + x] * kernel[k + radius];
      }
      output[row + x] = acc;
    }
  }
  return output;
}

function lanczos3(x: number): number {
  const ax = Math.abs(x);
  if (ax === 0) return 1;
  if (ax >= 3) return 0;
  const pix = Math.PI * ax;
  return (3 * Math.sin(pix) * Math.sin(pix / 3)) / (pix * pix);
}

/** Separable Lanczos-3 resize with premultiplied alpha (matching Pillow LANCZOS). */
function resizeLanczosRgba(
  src: RgbaImage,
  targetWidth: number,
  targetHeight: number
): RgbaImage {
  const premul = new Float32Array(src.width * src.height * 4);
  for (let i = 0; i < src.width * src.height; i++) {
    const o = i * 4;
    const a = src.data[o + 3] / 255;
    premul[o] = src.data[o] * a;
    premul[o + 1] = src.data[o + 1] * a;
    premul[o + 2] = src.data[o + 2] * a;
    premul[o + 3] = src.data[o + 3];
  }

  const horiz = new Float32Array(targetWidth * src.height * 4);
  const scaleX = src.width / targetWidth;
  const supportX = Math.max(1, scaleX) * 3;
  const filterScaleX = Math.max(1, scaleX);

  for (let dx = 0; dx < targetWidth; dx++) {
    const center = (dx + 0.5) * scaleX - 0.5;
    const left = Math.max(0, Math.floor(center - supportX));
    const right = Math.min(src.width - 1, Math.ceil(center + supportX));
    const weights: number[] = [];
    let weightSum = 0;
    for (let sx = left; sx <= right; sx++) {
      const w = lanczos3((sx - center) / filterScaleX);
      weights.push(w);
      weightSum += w;
    }
    for (let y = 0; y < src.height; y++) {
      let r = 0;
      let g = 0;
      let b = 0;
      let a = 0;
      const srcRow = y * src.width;
      for (let sx = left; sx <= right; sx++) {
        const w = weights[sx - left];
        const o = (srcRow + sx) * 4;
        r += premul[o] * w;
        g += premul[o + 1] * w;
        b += premul[o + 2] * w;
        a += premul[o + 3] * w;
      }
      const dst = (y * targetWidth + dx) * 4;
      horiz[dst] = r / weightSum;
      horiz[dst + 1] = g / weightSum;
      horiz[dst + 2] = b / weightSum;
      horiz[dst + 3] = a / weightSum;
    }
  }

  const out = new Uint8Array(targetWidth * targetHeight * 4);
  const scaleY = src.height / targetHeight;
  const supportY = Math.max(1, scaleY) * 3;
  const filterScaleY = Math.max(1, scaleY);

  for (let dy = 0; dy < targetHeight; dy++) {
    const center = (dy + 0.5) * scaleY - 0.5;
    const top = Math.max(0, Math.floor(center - supportY));
    const bottom = Math.min(src.height - 1, Math.ceil(center + supportY));
    const weights: number[] = [];
    let weightSum = 0;
    for (let sy = top; sy <= bottom; sy++) {
      const w = lanczos3((sy - center) / filterScaleY);
      weights.push(w);
      weightSum += w;
    }
    for (let dx = 0; dx < targetWidth; dx++) {
      let r = 0;
      let g = 0;
      let b = 0;
      let a = 0;
      for (let sy = top; sy <= bottom; sy++) {
        const w = weights[sy - top];
        const o = (sy * targetWidth + dx) * 4;
        r += horiz[o] * w;
        g += horiz[o + 1] * w;
        b += horiz[o + 2] * w;
        a += horiz[o + 3] * w;
      }
      const alpha = Math.min(255, Math.max(0, a / weightSum));
      const dst = (dy * targetWidth + dx) * 4;
      if (alpha > 0) {
        const unpremul = 255 / alpha;
        out[dst] = clampByte((r / weightSum) * unpremul);
        out[dst + 1] = clampByte((g / weightSum) * unpremul);
        out[dst + 2] = clampByte((b / weightSum) * unpremul);
        out[dst + 3] = clampByte(alpha);
      }
    }
  }

  return { width: targetWidth, height: targetHeight, data: out };
}

export function adaptiveXml(sha: string): string {
  return `<?xml version="1.0" encoding="utf-8"?>
<!-- The desktop app icon as an adaptive icon, generated by design/launcher-icon.ts from
     bazaarplusplus-installer/src-tauri/icons/app-icon.png (sha256 ${sha}).
     Do not edit: re-run \`just design::launcher-icon\` when the desktop icon changes.
     Themed icons use the flat brand mark. -->
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
  <background android:drawable="@mipmap/ic_launcher_background" />
  <foreground android:drawable="@mipmap/ic_launcher_foreground" />
  <monochrome android:drawable="@drawable/bpp_launcher_monochrome" />
</adaptive-icon>
`;
}

export interface ExportLauncherIconOptions {
  sourcePath?: string;
  resDir?: string;
}

export function exportLauncherIcon({
  sourcePath = DEFAULT_SOURCE,
  resDir = DEFAULT_RES
}: ExportLauncherIconOptions = {}): { sha256: string; files: string[] } {
  const sourceBytes = fs.readFileSync(sourcePath);
  const sha256 = crypto.createHash('sha256').update(sourceBytes).digest('hex');
  const icon = decodePngToRgba(sourceBytes);
  if (icon.width !== icon.height) {
    throw new Error(
      `Expected a square app icon, got ${icon.width}x${icon.height}.`
    );
  }
  const size = icon.width;
  const pixelAt = (x: number, y: number): [number, number, number, number] => {
    const o = (y * size + x) * 4;
    return [icon.data[o], icon.data[o + 1], icon.data[o + 2], icon.data[o + 3]];
  };

  // Fit the vertical gradient fill as c(y) = a + b * y over the left margin.
  const fillXStart = Math.min(
    size - 1,
    Math.round((FILL_X_START * size) / 1024)
  );
  const fillXEnd = Math.max(
    fillXStart + 1,
    Math.min(size, Math.round((FILL_X_END * size) / 1024))
  );
  const fillYStart = Math.min(
    size - 1,
    Math.round((FILL_Y_START * size) / 1024)
  );
  const fillYEnd = Math.max(
    fillYStart + 2,
    Math.min(size, Math.round((FILL_Y_END * size) / 1024))
  );

  let sampleCount = 0;
  let sumY = 0;
  const sumC = [0, 0, 0];
  for (let y = fillYStart; y < fillYEnd; y++) {
    for (let x = fillXStart; x < fillXEnd; x++) {
      const [r, g, b] = pixelAt(x, y);
      sampleCount++;
      sumY += y;
      sumC[0] += r;
      sumC[1] += g;
      sumC[2] += b;
    }
  }
  const meanY = sumY / sampleCount;
  const meanC = sumC.map((total) => total / sampleCount);
  let varY = 0;
  const cov = [0, 0, 0];
  for (let y = fillYStart; y < fillYEnd; y++) {
    const dy = y - meanY;
    for (let x = fillXStart; x < fillXEnd; x++) {
      const [r, g, b] = pixelAt(x, y);
      varY += dy * dy;
      cov[0] += dy * (r - meanC[0]);
      cov[1] += dy * (g - meanC[1]);
      cov[2] += dy * (b - meanC[2]);
    }
  }
  const fit: readonly [number, number][] = [0, 1, 2].map((channel) => {
    const slope = varY === 0 ? 0 : cov[channel] / varY;
    return [meanC[channel] - slope * meanY, slope] as const;
  });

  const fill = (y: number): [number, number, number] => [
    clampByte(fit[0][0] + fit[0][1] * y),
    clampByte(fit[1][0] + fit[1][1] * y),
    clampByte(fit[2][0] + fit[2][1] * y)
  ];

  // Find artwork pixels differing clearly from the gradient fill inside the rim.
  const inset = Math.floor(size / 16);
  const art: [number, number][] = [];
  for (let y = inset; y < size - inset; y += 2) {
    const [fr, fg, fb] = fill(y);
    for (let x = inset; x < size - inset; x += 2) {
      const [r, g, b] = pixelAt(x, y);
      if (Math.max(Math.abs(r - fr), Math.abs(g - fg), Math.abs(b - fb)) > 24) {
        art.push([x, y]);
      }
    }
  }
  if (art.length === 0) {
    throw new Error('Could not locate foreground artwork in the app icon.');
  }
  let left = size;
  let right = 0;
  let top = size;
  let bottom = 0;
  for (const [x, y] of art) {
    if (x < left) left = x;
    if (x > right) right = x;
    if (y < top) top = y;
    if (y > bottom) bottom = y;
  }
  const cx = (left + right) / 2;
  const cy = (top + bottom) / 2;
  let reach = 0;
  for (const [x, y] of art) {
    const dist = Math.hypot(x - cx, y - cy);
    if (dist > reach) reach = dist;
  }

  // Feather the squircle's rim away so only fill and artwork remain.
  const mask = gaussianBlur(
    roundedRectMask(size, inset, Math.floor(size / 6)),
    size,
    size,
    Math.floor(size / 48)
  );
  const foregroundData = new Uint8Array(icon.data);
  for (let i = 0; i < size * size; i++) {
    foregroundData[i * 4 + 3] = clampByte(foregroundData[i * 4 + 3] * mask[i]);
  }
  const foreground: RgbaImage = {
    width: size,
    height: size,
    data: foregroundData
  };

  const written: string[] = [];
  for (const [name, density] of DENSITIES) {
    const layer = Math.round(LAYER_DP * density);
    const scale = (SAFE_RADIUS_DP * density) / reach;
    const artSize = Math.round(size * scale);
    const offsetX = Math.round(layer / 2 - cx * scale);
    const offsetY = Math.round(layer / 2 - cy * scale);

    const resized = resizeLanczosRgba(foreground, artSize, artSize);
    const front = new Uint8Array(layer * layer * 4);
    for (let sy = 0; sy < artSize; sy++) {
      const dy = sy + offsetY;
      if (dy < 0 || dy >= layer) continue;
      for (let sx = 0; sx < artSize; sx++) {
        const dx = sx + offsetX;
        if (dx < 0 || dx >= layer) continue;
        const src = (sy * artSize + sx) * 4;
        const dst = (dy * layer + dx) * 4;
        front[dst] = resized.data[src];
        front[dst + 1] = resized.data[src + 1];
        front[dst + 2] = resized.data[src + 2];
        front[dst + 3] = resized.data[src + 3];
      }
    }

    const back = new Uint8Array(layer * layer * 3);
    for (let y = 0; y < layer; y++) {
      const [r, g, b] = fill((y - offsetY) / scale);
      const row = y * layer * 3;
      for (let x = 0; x < layer; x++) {
        back[row + x * 3] = r;
        back[row + x * 3 + 1] = g;
        back[row + x * 3 + 2] = b;
      }
    }

    const folder = path.join(resDir, `mipmap-${name}`);
    fs.mkdirSync(folder, { recursive: true });
    const frontPath = path.join(folder, 'ic_launcher_foreground.png');
    const backPath = path.join(folder, 'ic_launcher_background.png');
    fs.writeFileSync(frontPath, encodePng(layer, layer, front, 4));
    fs.writeFileSync(backPath, encodePng(layer, layer, back, 3));
    written.push(frontPath, backPath);
  }

  const adaptivePath = path.join(resDir, 'mipmap-anydpi', 'ic_launcher.xml');
  fs.mkdirSync(path.dirname(adaptivePath), { recursive: true });
  fs.writeFileSync(adaptivePath, adaptiveXml(sha256));
  written.push(adaptivePath);
  return { sha256, files: written };
}

if (import.meta.main) {
  exportLauncherIcon();
  console.log(
    `Exported the launcher icon from ${path.relative(REPO_ROOT, DEFAULT_SOURCE)}`
  );
}
