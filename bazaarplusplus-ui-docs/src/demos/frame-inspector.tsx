import { FrameInspector, gameTerms } from '@bpp/ui';
import run from '../demo-data/run.json';
import type { DemoProps } from '../copy';
export default function Frames({ lang }: DemoProps) {
  return (
    <FrameInspector
      frames={run.moments.map((m) => ({ ...m, message: null }))}
      read={(m) => m.frame}
      labels={gameTerms[lang].replay}
      exportFrames={async () => [
        '[' + run.moments.map((m) => m.frame).join(',') + ']'
      ]}
      download={(name, parts) => {
        const a = document.createElement('a');
        a.href = URL.createObjectURL(
          new Blob(
            parts.map((p) => (typeof p === 'string' ? p : new Uint8Array(p)))
          )
        );
        a.download = name;
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 0);
      }}
    />
  );
}
