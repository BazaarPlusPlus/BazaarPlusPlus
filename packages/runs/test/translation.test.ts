import { describe, expect, test } from 'vite-plus/test';
import {
  SchemaIndex,
  translateMessage,
  countOutputLeaves
} from '../message-stream/translate';
import { MessageStream } from '../message-stream/stream';
import { readable, stringifyJson } from '../message-stream/readable';
import { decodeMessagePackValue } from '../msgpack';
import { SCHEMA } from './fixtures/game-messages';
import { encodeMessagePack } from './fixtures/encode';
describe('Translation rules', () => {
  const index = new SchemaIndex(SCHEMA);
  const translate = (message: unknown) =>
    translateMessage(index, decodeMessagePackValue(encodeMessagePack(message)));

  test('a value at an index no build names is kept raw and flagged', () => {
    const { value, flags } = translate([1, ['err', 0, 'text', 'new field']]);
    expect(stringifyJson(readable(value), 0).replace(/\n */g, '')).toContain(
      '"$extra": {"3": {"$raw": "new field","$flag": "unnamed index"}}'
    );
    expect(flags).toEqual([{ path: 'Message[3]', reason: 'unnamed index' }]);
    expect(countOutputLeaves(value)).toBe(5);
  });

  test('an unknown union case, enum value or shape is kept raw and flagged', () => {
    expect(translate([99, ['x']]).flags).toEqual([
      { path: 'Message', reason: 'unknown INetMessage case 99' }
    ]);
    const unknownEnum = translate([1, ['err', 12345, 'text']]);
    expect(readable(unknownEnum.value)).toMatchObject({
      ErrorType: '? (12345)'
    });
    expect(unknownEnum.flags).toEqual([
      { path: 'Message.ErrorType', reason: 'unknown EResponseErrorType value' }
    ]);
    const shape = translate([1, ['err', 'not a number', 'text']]);
    expect(readable(shape.value)).toMatchObject({
      ErrorType: { $raw: 'not a number', $flag: 'expected EResponseErrorType' }
    });
  });

  test('a key two builds name differently is shown with both names, not one', () => {
    const conflicted = structuredClone(SCHEMA);
    const error = Object.entries(conflicted.types).find(([n]) =>
      n.endsWith('.NetMessageError')
    )![1];
    if (error.kind !== 'object')
      throw new Error('NetMessageError is not an object');
    error.members.push({
      key: 2,
      name: 'Text',
      type: 'System.String',
      sources: ['macos-99999']
    });
    const { value, flags } = translateMessage(
      new SchemaIndex(conflicted),
      decodeMessagePackValue(encodeMessagePack([1, ['err', 0, 'text']]))
    );
    expect(readable(value)).toMatchObject({
      $extra: {
        '2': {
          $raw: 'text',
          $flag: 'index 2 is Message: String or Text: String across builds'
        }
      }
    });
    expect(flags).toHaveLength(1);
  });

  test('a 64-bit value prints exactly in readable JSON', () => {
    const value = translate([1, ['err', 0, 'text']]).value;
    expect(
      stringifyJson({ big: 9007199254740993n, value: readable(value) }, 0)
    ).toContain('9007199254740993');
  });

  test('a member at another index in another build is kept raw at both indices', () => {
    const moved = structuredClone(SCHEMA);
    const error = Object.entries(moved.types).find(([n]) =>
      n.endsWith('.NetMessageError')
    )![1];
    if (error.kind !== 'object')
      throw new Error('NetMessageError is not an object');
    error.members.push({
      key: 1,
      name: 'MessageId',
      type: 'System.String',
      sources: ['macos-99999']
    });
    const { value, flags } = translateMessage(
      new SchemaIndex(moved),
      decodeMessagePackValue(encodeMessagePack([1, ['7', null, 'text']]))
    );
    expect(readable(value)).toMatchObject({
      $extra: {
        '0': { $raw: '7', $flag: 'MessageId is at index 0 or 1 across builds' },
        '1': { $raw: null, $flag: 'MessageId is at index 0 or 1 across builds' }
      }
    });
    expect(readable(value)).not.toHaveProperty('MessageId');
    expect(flags).toHaveLength(2);
  });

  test('readable JSON keeps each union tag with the qualified type', () => {
    const { value } = translate([1, ['err', 0, 'text']]);
    expect(readable(value)).toMatchObject({
      $type: 'BazaarGameShared.Infra.Messages.NetMessageError',
      $tag: 1
    });
  });
});

describe('Frames as a reader sees them', () => {
  test('CommandArgsJson stays the recorded text; a parse that would round it is left out', () => {
    const frames = [
      {
        seq: 1,
        timestampMs: 0,
        message: null,
        respondsToSeq: null,
        command: 'SendSelectItem',
        commandArgsJson: '{"instanceId":9007199254740993}'
      },
      {
        seq: 2,
        timestampMs: 0,
        message: null,
        respondsToSeq: null,
        command: 'SendSellCard',
        commandArgsJson: '{"cardId":"itm_a"}'
      }
    ];
    const s = new MessageStream(frames, SCHEMA, 'desktop');
    const big = s.readableFrame(frames[0]);
    expect(big).toContain(
      '"CommandArgsJson": "{\\"instanceId\\":9007199254740993}"'
    );
    expect(big).not.toContain('"CommandArgs":');
    expect(JSON.parse(s.readableFrame(frames[1]))).toMatchObject({
      CommandArgsJson: '{"cardId":"itm_a"}',
      CommandArgs: { cardId: 'itm_a' }
    });
  });
});
