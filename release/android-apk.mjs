import crypto from 'node:crypto';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

// Reads the facts an Interop Snapshot records from an official Android APK
// (root GLOSSARY.md, ADR 0008): the entries interop generation consumes, the
// package versionName and versionCode from the binary AndroidManifest.xml, and
// the Unity version from the data.unity3d bundle header. Nothing here needs the
// Android SDK; entries are read through `unzip`.

export const APK_ENTRIES = Object.freeze({
  il2cpp: 'lib/arm64-v8a/libil2cpp.so',
  metadata: 'assets/bin/Data/Managed/Metadata/global-metadata.dat',
  manifest: 'AndroidManifest.xml',
  dataBundle: 'assets/bin/Data/data.unity3d'
});

export async function sha256File(file) {
  const hash = crypto.createHash('sha256');
  for await (const chunk of fs.createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

// Copies one APK entry to a file; a missing entry fails naming it.
export function extractApkEntry(apk, entry, into) {
  try {
    const bytes = execFileSync('unzip', ['-p', apk, entry], {
      maxBuffer: 1024 * 1024 * 1024,
      stdio: ['ignore', 'pipe', 'pipe']
    });
    if (bytes.length === 0) throw new Error('empty entry');
    fs.writeFileSync(into, bytes);
  } catch (error) {
    throw new Error(
      `Cannot read ${entry} from ${apk}: ${String(error.stderr || error.message).trim()}`
    );
  }
}

const RES_STRING_POOL = 0x0001;
const RES_XML = 0x0003;
const RES_XML_START_ELEMENT = 0x0102;
const UTF8_FLAG = 1 << 8;
const TYPE_STRING = 0x03;
const TYPE_INT_DEC = 0x10;
const TYPE_INT_HEX = 0x11;

function stringPool(bytes, start) {
  const count = bytes.readUInt32LE(start + 8);
  const utf8 = (bytes.readUInt32LE(start + 16) & UTF8_FLAG) !== 0;
  const stringsStart = start + bytes.readUInt32LE(start + 20);
  const headerSize = bytes.readUInt16LE(start + 2);
  const strings = [];
  for (let index = 0; index < count; index++) {
    let at = stringsStart + bytes.readUInt32LE(start + headerSize + index * 4);
    if (utf8) {
      // UTF-16 length, then UTF-8 length; each one or two bytes.
      at += bytes[at] & 0x80 ? 2 : 1;
      let length = bytes[at];
      if (length & 0x80) {
        length = ((length & 0x7f) << 8) | bytes[at + 1];
        at += 2;
      } else at += 1;
      strings.push(bytes.toString('utf8', at, at + length));
    } else {
      let length = bytes.readUInt16LE(at);
      if (length & 0x8000) {
        length = ((length & 0x7fff) << 16) | bytes.readUInt16LE(at + 2);
        at += 4;
      } else at += 2;
      strings.push(bytes.toString('utf16le', at, at + length * 2));
    }
  }
  return strings;
}

// The <manifest> element's versionName and versionCode from a compiled
// (binary XML) AndroidManifest.xml.
export function parseAndroidManifest(bytes) {
  if (bytes.length < 8 || bytes.readUInt16LE(0) !== RES_XML)
    throw new Error('AndroidManifest.xml is not a compiled binary XML file');
  let strings = null;
  let at = bytes.readUInt16LE(2);
  while (at + 8 <= bytes.length) {
    const type = bytes.readUInt16LE(at);
    const headerSize = bytes.readUInt16LE(at + 2);
    const size = bytes.readUInt32LE(at + 4);
    if (size < 8) break;
    if (type === RES_STRING_POOL && strings === null)
      strings = stringPool(bytes, at);
    if (type === RES_XML_START_ELEMENT && strings) {
      const name = strings[bytes.readUInt32LE(at + 20)];
      if (name === 'manifest') {
        const attributeStart = bytes.readUInt16LE(at + 24);
        const attributeSize = bytes.readUInt16LE(at + 26);
        const attributeCount = bytes.readUInt16LE(at + 28);
        const attributes = {};
        for (let index = 0; index < attributeCount; index++) {
          const attribute =
            at + headerSize + attributeStart + index * attributeSize;
          const key = strings[bytes.readUInt32LE(attribute + 4)];
          const raw = bytes.readUInt32LE(attribute + 8);
          const dataType = bytes[attribute + 15];
          const data = bytes.readUInt32LE(attribute + 16);
          attributes[key] =
            dataType === TYPE_STRING
              ? strings[data]
              : dataType === TYPE_INT_DEC || dataType === TYPE_INT_HEX
                ? data
                : raw !== 0xffffffff
                  ? strings[raw]
                  : data;
        }
        const versionCode = Number(attributes.versionCode);
        if (typeof attributes.versionName !== 'string' || !versionCode)
          throw new Error(
            'AndroidManifest.xml <manifest> carries no versionName or versionCode'
          );
        return { versionName: attributes.versionName, versionCode };
      }
    }
    at += size;
  }
  throw new Error('AndroidManifest.xml has no <manifest> element');
}

// A UnityFS bundle starts "UnityFS\0", a big-endian u32 format number, the
// engine version family ("5.x.x\0") and the Unity version that built it
// ("6000.3.11f1\0").
export function parseUnityBundleVersion(bytes) {
  const signature = 'UnityFS\0';
  if (bytes.subarray(0, signature.length).toString('latin1') !== signature)
    throw new Error('data.unity3d is not a UnityFS bundle');
  const [, version] = bytes
    .subarray(signature.length + 4, 128)
    .toString('latin1')
    .split('\0');
  if (!/^\d+\.\d+\.\d+[abfp]\d+$/.test(version ?? ''))
    throw new Error(
      `data.unity3d names no Unity version (${JSON.stringify(version)})`
    );
  return version;
}

function readHead(file, length) {
  const handle = fs.openSync(file, 'r');
  try {
    const bytes = Buffer.alloc(length);
    const read = fs.readSync(handle, bytes, 0, length, 0);
    return bytes.subarray(0, read);
  } finally {
    fs.closeSync(handle);
  }
}

// Extracts the generation inputs into `into` and reads the APK's identity.
export async function readApk(apk, into) {
  const files = {
    il2cpp: `${into}/libil2cpp.so`,
    metadata: `${into}/global-metadata.dat`,
    manifest: `${into}/AndroidManifest.xml`,
    dataBundle: `${into}/data.unity3d`
  };
  for (const [name, file] of Object.entries(files))
    extractApkEntry(apk, APK_ENTRIES[name], file);
  const { versionName, versionCode } = parseAndroidManifest(
    fs.readFileSync(files.manifest)
  );
  return {
    versionName,
    versionCode,
    unityVersion: parseUnityBundleVersion(readHead(files.dataBundle, 128)),
    apkSha256: await sha256File(apk),
    il2cppSha256: await sha256File(files.il2cpp),
    metadataSha256: await sha256File(files.metadata),
    il2cpp: files.il2cpp,
    metadata: files.metadata
  };
}
