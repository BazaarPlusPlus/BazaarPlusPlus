import { FrameInspector } from '@bpp/ui';
import run from '../demo-data/run.json';
import { type DemoProps, translate } from '../copy';
export default function Frames({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <FrameInspector
      frames={run.moments.map((m) => ({ ...m, message: null }))}
      read={(m) => m.frame}
      labels={{
        rawFrames: t('原始帧', 'Raw frames'),
        frame: t('帧', 'Frame'),
        downloadBytes: t('下载字节', 'Download bytes'),
        downloadAll: t('下载全部', 'Download all')
      }}
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
