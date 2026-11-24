// @vitest-environment node

import { describe, expect, test } from 'vite-plus/test';

import { decompressLz4Block } from '../src/features/runs/message-stream/lz4';
import { unwrapMessage } from '../src/features/runs/message-stream/wire';
import {
  decodeMessagePack,
  decodeMessagePackValue,
  MpExt,
  MpMap
} from '../src/features/runs/msgpack';
import {
  compressLz4Block,
  wrapLz4BlockArray
} from '../tools/run-fixture/rewrite';
import { encodeMessagePack } from './fixtures/run-payload';

const bytes = (...values: number[]) => Uint8Array.from(values);
const text = (value: string) => new TextEncoder().encode(value);

describe('LZ4 block', () => {
  test('reads a literal-only block', () => {
    expect(decompressLz4Block(bytes(0x50, ...text('hello')), 5)).toEqual(
      text('hello')
    );
  });

  test('reads an overlapping match, which repeats what it is writing', () => {
    // "a", then a match at offset 1 of length 4 + 5, then the 5 closing literals.
    const block = bytes(0x15, 0x61, 0x01, 0x00, 0x50, ...text('bcdef'));
    expect(new TextDecoder().decode(decompressLz4Block(block, 15))).toBe(
      'aaaaaaaaaabcdef'
    );
  });

  test('reads extended literal and match lengths', () => {
    const literals = Array.from({ length: 20 }, (_, i) => 0x41 + (i % 26));
    // 20 literals (15 + 5), then a 300-byte match at offset 20 (4 + 15 + 255 + 26).
    const block = bytes(
      0xff,
      5,
      ...literals,
      20,
      0,
      255,
      26,
      0x50,
      ...text('vwxyz')
    );
    const out = decompressLz4Block(block, 20 + 300 + 5);
    expect(out.subarray(0, 20)).toEqual(Uint8Array.from(literals));
    expect(out[20 + 299]).toBe(literals[299 % 20]);
    expect(new TextDecoder().decode(out.subarray(320))).toBe('vwxyz');
  });

  test('rejects an offset before the start and a length mismatch', () => {
    expect(() => decompressLz4Block(bytes(0x10, 0x61, 0x05, 0x00), 5)).toThrow(
      'Invalid LZ4 offset'
    );
    expect(() => decompressLz4Block(bytes(0x50, ...text('hello')), 6)).toThrow(
      'LZ4 block length mismatch'
    );
    expect(() => decompressLz4Block(bytes(0xf0, 0xff), 300)).toThrow(
      'Truncated LZ4 length'
    );
  });

  test('round-trips through the fixture compressor', () => {
    const input = text('abcabcabcabc'.repeat(200) + 'tail of the block');
    const block = compressLz4Block(input);
    expect(block.length).toBeLessThan(input.length / 4);
    expect(decompressLz4Block(block, input.length)).toEqual(input);
  });
});

describe('Message wire format', () => {
  const message = encodeMessagePack([3, [['x'.repeat(40), 7], 'abc']]);

  test('a frame below 1024 bytes is plain MessagePack', () => {
    expect(unwrapMessage(message)).toEqual({ bytes: message, blocks: [] });
  });

  test('a single-block Lz4BlockArray frame', () => {
    const frame = wrapLz4BlockArray(message);
    expect(frame[0]).toBe(0x92);
    expect(unwrapMessage(frame)).toEqual({
      bytes: message,
      blocks: [message.length]
    });
  });

  test('a multi-block Lz4BlockArray frame joins its blocks in order', () => {
    const frame = wrapLz4BlockArray(message, [10, 20]);
    expect(frame[0]).toBe(0x94);
    expect(unwrapMessage(frame)).toEqual({
      bytes: message,
      blocks: [10, 20, message.length - 30]
    });
  });

  test('a block count that disagrees with its header fails', () => {
    const frame = wrapLz4BlockArray(message, [10]);
    // Drop the second block's bin from the array header count.
    frame[0] = 0x92;
    expect(() => unwrapMessage(frame)).toThrow('Trailing MessagePack bytes');
  });
});

describe('Lossless MessagePack', () => {
  test('keeps a 64-bit integer as a bigint before any Number conversion', () => {
    const big = bytes(0xcf, 0x00, 0x20, 0, 0, 0, 0, 0, 0x01);
    expect(decodeMessagePackValue(big)).toBe(9007199254740993n);
    expect(() => decodeMessagePack(big)).toThrow('Unsafe MessagePack integer');
    const safe = bytes(0xd3, 0xff, 0xe0, 0, 0, 0, 0, 0, 0x01);
    expect(decodeMessagePackValue(safe)).toBe(-9007199254740991);
  });

  test('keeps map key types and order', () => {
    const value = decodeMessagePackValue(
      encodeMessagePack(
        new Map<unknown, unknown>([
          [4, 'gold'],
          ['4', 'text'],
          [-1, null]
        ])
      )
    );
    expect(value).toBeInstanceOf(MpMap);
    expect((value as MpMap).entries).toEqual([
      [4, 'gold'],
      ['4', 'text'],
      [-1, null]
    ]);
  });

  test('reads every extension header form', () => {
    expect(decodeMessagePackValue(bytes(0xd6, 0x62, 1, 2, 3, 4))).toEqual(
      new MpExt(98, bytes(1, 2, 3, 4))
    );
    expect(decodeMessagePackValue(bytes(0xc7, 0x03, 0xff, 7, 8, 9))).toEqual(
      new MpExt(-1, bytes(7, 8, 9))
    );
  });
});
