import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { test } from 'vite-plus/test';
import {
  adaptiveXml,
  decodePngToRgba,
  encodePng,
  exportLauncherIcon
} from './launcher-icon.ts';

const REPO_ROOT = path.resolve(import.meta.dirname, '..');

function make16BitRgbaPng(width: number, height: number): Buffer {
  const signature = Buffer.from([
    0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
  ]);
  const crcTable = Uint32Array.from({ length: 256 }, (_, i) => {
    let v = i;
    for (let b = 0; b < 8; b++) v = v & 1 ? 0xedb88320 ^ (v >>> 1) : v >>> 1;
    return v >>> 0;
  });
  const chunk = (type: string, body: Buffer): Buffer => {
    const head = Buffer.alloc(4);
    head.writeUInt32BE(body.length, 0);
    const tagged = Buffer.concat([Buffer.from(type, 'latin1'), body]);
    let c = 0xffffffff;
    for (const byte of tagged) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8);
    const tail = Buffer.alloc(4);
    tail.writeUInt32BE((c ^ 0xffffffff) >>> 0, 0);
    return Buffer.concat([head, tagged, tail]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 16;
  ihdr[9] = 6;
  const stride = width * 8;
  const raw = Buffer.alloc((stride + 1) * height);
  for (let y = 0; y < height; y++) {
    const row = y * (stride + 1);
    raw[row] = 0;
    for (let x = 0; x < width; x++) {
      const o = row + 1 + x * 8;
      // Dark vertical gradient background with bright center artwork (~60% span).
      const inArt =
        Math.abs(x - width / 2) < width * 0.3 &&
        Math.abs(y - height / 2) < height * 0.3;
      const r = inArt ? 240 : 20;
      const g = inArt ? 180 : 20 + Math.round((y / height) * 16);
      const b = inArt ? 40 : 24;
      raw.writeUInt16BE(r * 257, o);
      raw.writeUInt16BE(g * 257, o + 2);
      raw.writeUInt16BE(b * 257, o + 4);
      raw.writeUInt16BE(65535, o + 6);
    }
  }
  return Buffer.concat([
    signature,
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0))
  ]);
}

test('decodePngToRgba reads 16-bit RGBA PNGs and encodePng round-trips RGB and RGBA', () => {
  const png16 = make16BitRgbaPng(16, 16);
  const decoded = decodePngToRgba(png16);
  assert.equal(decoded.width, 16);
  assert.equal(decoded.height, 16);
  // Center pixel is bright amber artwork; corner is dark background.
  const center = (8 * 16 + 8) * 4;
  assert.equal(decoded.data[center], 240);
  assert.equal(decoded.data[center + 1], 180);
  assert.equal(decoded.data[center + 2], 40);
  assert.equal(decoded.data[center + 3], 255);
  assert.equal(decoded.data[0], 20);

  const reEncodedRgba = decodePngToRgba(encodePng(16, 16, decoded.data, 4));
  assert.deepEqual(reEncodedRgba.data, decoded.data);

  const rgb = new Uint8Array(16 * 16 * 3);
  for (let i = 0; i < 16 * 16; i++) {
    rgb[i * 3] = decoded.data[i * 4];
    rgb[i * 3 + 1] = decoded.data[i * 4 + 1];
    rgb[i * 3 + 2] = decoded.data[i * 4 + 2];
  }
  const reEncodedRgb = decodePngToRgba(encodePng(16, 16, rgb, 3));
  assert.equal(reEncodedRgb.data[center], 240);
  assert.equal(reEncodedRgb.data[center + 3], 255);
});

test('exportLauncherIcon handles a 16-bit RGBA icon and writes all densities plus adaptive XML', (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-launcher-icon-'));
  t.onTestFinished(() => fs.rmSync(dir, { recursive: true, force: true }));
  const source = path.join(dir, 'app-icon.png');
  const res = path.join(dir, 'res');
  fs.writeFileSync(source, make16BitRgbaPng(128, 128));

  const { sha256, files } = exportLauncherIcon({
    sourcePath: source,
    resDir: res
  });
  assert.match(sha256, /^[0-9a-f]{64}$/);
  assert.equal(files.length, 11);

  const xml = fs.readFileSync(
    path.join(res, 'mipmap-anydpi', 'ic_launcher.xml'),
    'utf8'
  );
  assert.equal(xml, adaptiveXml(sha256));
  assert.match(xml, new RegExp(`sha256 ${sha256}`));

  const mdpiFront = decodePngToRgba(
    fs.readFileSync(path.join(res, 'mipmap-mdpi', 'ic_launcher_foreground.png'))
  );
  assert.equal(mdpiFront.width, 108);
  assert.equal(mdpiFront.height, 108);
  // Outer corners of the foreground layer are transparent; center has opaque artwork.
  assert.equal(mdpiFront.data[3], 0);
  assert.equal(mdpiFront.data[(54 * 108 + 54) * 4 + 3], 255);

  const xxxhdpiBack = decodePngToRgba(
    fs.readFileSync(
      path.join(res, 'mipmap-xxxhdpi', 'ic_launcher_background.png')
    )
  );
  assert.equal(xxxhdpiBack.width, 432);
  assert.equal(xxxhdpiBack.height, 432);
});

test('decodePngToRgba decodes the real 16-bit desktop app-icon.png', () => {
  const icon = decodePngToRgba(
    fs.readFileSync(
      path.join(
        REPO_ROOT,
        'bazaarplusplus-installer/src-tauri/icons/app-icon.png'
      )
    )
  );
  assert.equal(icon.width, 1024);
  assert.equal(icon.height, 1024);
});
