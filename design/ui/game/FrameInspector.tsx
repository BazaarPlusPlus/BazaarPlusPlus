import { useState } from 'react';
import { Button } from '../Button';

export function FrameInspector<
  F extends { seq: number; message: Uint8Array | null }
>({
  frames,
  read,
  download,
  exportFrames,
  labels
}: {
  frames: readonly F[];
  read: (frame: F) => string;
  download: (name: string, parts: (Uint8Array | string)[]) => void;
  exportFrames: () => Promise<string[]>;
  labels: {
    rawFrames: string;
    frame: string;
    downloadBytes: string;
    downloadAll: string;
  };
}) {
  const [open, setOpen] = useState(false);
  const [seq, setSeq] = useState(frames[0]?.seq);
  const [busy, setBusy] = useState(false);
  const frame = frames.find((f) => f.seq === seq);
  const bytes = frame?.message;
  return (
    <details
      className="bpp-details"
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary>{labels.rawFrames}</summary>
      {open && (
        <div className="bpp-field">
          <label>
            {labels.frame}{' '}
            <select
              className="bpp-select"
              value={seq}
              onChange={(e) => setSeq(Number(e.target.value))}
            >
              {frames.map((f) => (
                <option key={f.seq}>{f.seq}</option>
              ))}
            </select>
          </label>
          {frame && <pre className="bpp-frame-json">{read(frame)}</pre>}
          <div className="bpp-field-row">
            {bytes && (
              <Button
                size="sm"
                onClick={() => download(`frame-${seq}.bin`, [bytes])}
              >
                {labels.downloadBytes}
              </Button>
            )}
            <Button
              size="sm"
              busy={busy}
              onClick={() => {
                setBusy(true);
                void exportFrames()
                  .then((parts) => download('run-message-stream.json', parts))
                  .finally(() => setBusy(false));
              }}
            >
              {labels.downloadAll}
            </Button>
          </div>
        </div>
      )}
    </details>
  );
}
