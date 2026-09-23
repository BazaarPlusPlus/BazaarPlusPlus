import { ChevronRight, Clipboard, ListX } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { useI18n } from '../../i18n/LocaleProvider';

export function ResetDataFailureDetails({ paths }: { paths: string[] }) {
  const { t } = useI18n();
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>(
    'idle'
  );

  if (paths.length === 0) return null;

  const diagnosticText = [
    'BazaarPlusPlus reset local data partial failure:',
    ...paths.map((path) => `- ${path}`)
  ].join('\n');

  const copyDiagnostics = () => {
    const write = navigator.clipboard?.writeText(diagnosticText);
    if (!write) {
      setCopyState('failed');
      window.setTimeout(() => setCopyState('idle'), 2400);
      return;
    }
    void write
      .then(() => {
        setCopyState('copied');
        window.setTimeout(() => setCopyState('idle'), 1600);
      })
      .catch(() => {
        setCopyState('failed');
        window.setTimeout(() => setCopyState('idle'), 2400);
      });
  };

  return (
    <details className="bpp-details selectable">
      <summary>
        <ChevronRight size={14} className="bpp-details-chevron" />
        <ListX size={14} />
        {t('resetDataFailureDetails')}
      </summary>
      <div className="mt-3 flex flex-col gap-3">
        <ul className="m-0 max-h-32 overflow-auto pl-4 font-mono leading-relaxed">
          {paths.map((path) => (
            <li key={path} className="break-all">
              {path}
            </li>
          ))}
        </ul>
        <Button
          size="sm"
          className="self-start"
          onClick={copyDiagnostics}
          icon={<Clipboard />}
        >
          {copyState === 'copied'
            ? t('resetDataFailureCopied')
            : copyState === 'failed'
              ? t('resetDataFailureCopyFailed')
              : t('resetDataFailureCopy')}
        </Button>
      </div>
    </details>
  );
}
