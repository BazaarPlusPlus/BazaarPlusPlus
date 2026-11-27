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
    />
  );
}
