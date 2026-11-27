import { decodeMessagePackValue } from '../msgpack';
import { base64, readable, stringifyJson } from './readable';
import { shortName, type GameMessageSchema } from './schema';
import {
  SchemaIndex,
  TObject,
  translateMessage,
  type Translated,
  type TranslationFlag
} from './translate';
import { unwrapMessage } from './wire';

/** One `RunMessageFrameV6` (Run segment key 8), with all six fields. */
export type StreamFrame = {
  seq: number;
  timestampMs: number;
  /** A view into the decoded Run segment; null on a command. */
  message: Uint8Array | null;
  respondsToSeq: number | null;
  command: string | null;
  commandArgsJson: string | null;
};

export type DecodedMessage =
  | {
      ok: true;
      /** The message type's short name, e.g. `NetMessageGameSim`. */
      type: string;
      value: Translated;
      flags: TranslationFlag[];
      /** LZ4 blocks the frame was stored in; 0 when uncompressed. */
      blocks: number;
    }
  | { ok: false; error: string };

/** Decoded trees kept for the frames a reader has open; the rest decode again on demand. */
const CACHED_MESSAGES = 16;
const EXPORT_BATCH = 32;

/**
 * The Run Message Stream, ordered by Seq. Messages decode on demand; only the last few stay
 * cached, so a projection over the whole Run does not keep every tree. A frame that fails
 * keeps its exact bytes and the error.
 */
export class MessageStream {
  readonly frames: readonly StreamFrame[];
  private readonly bySeq = new Map<number, StreamFrame>();
  private readonly decoded = new Map<number, DecodedMessage>();
  private readonly index: SchemaIndex;
  /** `android`: the layout was not read from [Key] attributes but matched by name. */
  readonly platform: string | null;

  constructor(
    frames: readonly StreamFrame[],
    schema: GameMessageSchema,
    platform: string | null
  ) {
    this.platform = platform;
    this.frames = [...frames].sort((a, b) => a.seq - b.seq);
    for (const frame of this.frames) this.bySeq.set(frame.seq, frame);
    this.index = new SchemaIndex(schema);
  }

  frame(seq: number): StreamFrame | undefined {
    return this.bySeq.get(seq);
  }

  /** How many decoded trees the stream holds now. */
  get cachedMessages(): number {
    return this.decoded.size;
  }

  message(frame: StreamFrame): DecodedMessage | null {
    if (frame.message === null) return null;
    let found = this.decoded.get(frame.seq);
    if (found) {
      // Most recently used last.
      this.decoded.delete(frame.seq);
      this.decoded.set(frame.seq, found);
    } else {
      try {
        const wire = unwrapMessage(frame.message);
        const { value, flags } = translateMessage(
          this.index,
          decodeMessagePackValue(wire.bytes)
        );
        found = {
          ok: true,
          type: value instanceof TObject ? shortName(value.type) : '?',
          value,
          flags,
          blocks: wire.blocks.length
        };
      } catch (error) {
        found = { ok: false, error: (error as Error).message };
      }
      this.decoded.set(frame.seq, found);
      if (this.decoded.size > CACHED_MESSAGES) {
        const oldest = this.decoded.keys().next();
        if (!oldest.done) this.decoded.delete(oldest.value);
      }
    }
    return found;
  }

  /**
   * The frame's six fields as readable JSON; a failed message as its bytes and error.
   * `CommandArgsJson` stays the recorded text; `CommandArgs` adds it parsed only when parsing
   * keeps it exactly (a 64-bit number would be rounded).
   */
  readableFrame(frame: StreamFrame): string {
    const decoded = this.message(frame);
    let args: unknown;
    if (frame.commandArgsJson !== null) {
      try {
        const parsed: unknown = JSON.parse(frame.commandArgsJson);
        if (JSON.stringify(parsed) === frame.commandArgsJson) args = parsed;
      } catch {
        // Kept only as the recorded text.
      }
    }
    const message =
      decoded === null || frame.message === null
        ? null
        : decoded.ok
          ? this.platform === 'android'
            ? { $layout: 'inferred', ...(readable(decoded.value) as object) }
            : readable(decoded.value)
          : { $bytes: base64(frame.message), $error: decoded.error };
    const out: Parameters<typeof stringifyJson>[0] = {
      Seq: frame.seq,
      TimestampMs: frame.timestampMs,
      Message: message as never,
      RespondsToSeq: frame.respondsToSeq,
      Command: frame.command,
      CommandArgsJson: frame.commandArgsJson
    };
    if (args !== undefined) out.CommandArgs = args as never;
    return stringifyJson(out);
  }

  /** Every frame as one readable JSON array, built in batches that yield to the browser. */
  async exportFrames(): Promise<string[]> {
    const parts: string[] = ['['];
    for (const [i, frame] of this.frames.entries()) {
      // oxlint-disable-next-line no-await-in-loop -- batches yield in order.
      if (i > 0 && i % EXPORT_BATCH === 0) await pause();
      parts.push(`${i ? ',\n' : ''}${this.readableFrame(frame)}`);
    }
    parts.push(']\n');
    return parts;
  }
}

const pause = () => new Promise<void>((resolve) => setTimeout(resolve, 0));
