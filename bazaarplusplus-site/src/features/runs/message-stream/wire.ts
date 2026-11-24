import { decodeMessagePackValue, MpExt, type MpValue } from '../msgpack';
import { decompressLz4Block } from './lz4';

/** MessagePack-CSharp's extension type for Lz4BlockArray. */
const LZ4_BLOCK_ARRAY = 98;
const MAX_MESSAGE_BYTES = 16 * 1024 * 1024;

/** The lengths in an Lz4BlockArray header: consecutive MessagePack integers. */
function blockLengths(data: Uint8Array): number[] {
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  const lengths: number[] = [];
  let at = 0;
  const need = (count: number) => {
    if (at + count > data.length) throw new Error('Truncated LZ4 header');
  };
  while (at < data.length) {
    const marker = data[at++];
    let length: number;
    if (marker < 0x80) length = marker;
    else if (marker === 0xcc || marker === 0xd0) {
      need(1);
      length = marker === 0xcc ? view.getUint8(at) : view.getInt8(at);
      at += 1;
    } else if (marker === 0xcd || marker === 0xd1) {
      need(2);
      length = marker === 0xcd ? view.getUint16(at) : view.getInt16(at);
      at += 2;
    } else if (marker === 0xce || marker === 0xd2) {
      need(4);
      length = marker === 0xce ? view.getUint32(at) : view.getInt32(at);
      at += 4;
    } else
      throw new Error(`Invalid LZ4 header marker 0x${marker.toString(16)}`);
    if (length < 0) throw new Error('Negative LZ4 block length');
    lengths.push(length);
  }
  return lengths;
}

export type WireMessage = {
  /** The MessagePack bytes the message was written as, decompressed. */
  bytes: Uint8Array;
  /** Each LZ4 block's decompressed length; empty when the frame was stored uncompressed. */
  blocks: number[];
};

/**
 * Undoes the game's `MessagePackConfig.Options` (Lz4BlockArray, compressing from 1024 bytes):
 * a compressed frame is an array of an ext 98 header holding each block's decompressed length,
 * then one bin per block; anything else is plain MessagePack.
 */
export function unwrapMessage(frame: Uint8Array): WireMessage {
  const outer = decodeMessagePackValue(frame);
  if (
    !Array.isArray(outer) ||
    !(outer[0] instanceof MpExt) ||
    outer[0].type !== LZ4_BLOCK_ARRAY
  ) {
    return { bytes: frame, blocks: [] };
  }
  const lengths = blockLengths(outer[0].data);
  if (lengths.length !== outer.length - 1) {
    throw new Error('LZ4 block count mismatch');
  }
  const total = lengths.reduce((sum, n) => sum + n, 0);
  if (total > MAX_MESSAGE_BYTES) throw new Error('Message exceeds 16 MiB');
  const bytes = new Uint8Array(total);
  let at = 0;
  lengths.forEach((length, index) => {
    const block: MpValue = outer[index + 1];
    if (!(block instanceof Uint8Array)) throw new Error('LZ4 block is not bin');
    bytes.set(decompressLz4Block(block, length), at);
    at += length;
  });
  return { bytes, blocks: lengths };
}
