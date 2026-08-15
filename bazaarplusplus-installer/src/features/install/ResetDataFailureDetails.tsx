import { ChevronRight, Clipboard, ListX } from 'lucide-react';
import { useState } from 'react';
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
    <details className="bpp-reset-failure-details group selectable px-3 py-2 text-xs">
      <summary className="cursor-pointer flex items-center gap-2 list-none">
        <ChevronRight
          size={14}
          className="shrink-0 transition-transform group-open:rotate-90"
        />
        <ListX size={14} />
        {t('resetDataFailureDetails')}
      </summary>
      <div className="mt-3 flex flex-col gap-3">
        <ul className="m-0 max-h-32 overflow-auto pl-4 leading-relaxed">
          {paths.map((path) => (
            <li key={path} className="break-all">
              {path}
            </li>
          ))}
        </ul>
        <button
          type="button"
          onClick={copyDiagnostics}
          className="bpp-reset-failure-copy self-start inline-flex items-center gap-2 px-3 py-1.5 transition-colors"
        >
          <Clipboard size={13} />
          {copyState === 'copied'
            ? t('resetDataFailureCopied')
            : copyState === 'failed'
              ? t('resetDataFailureCopyFailed')
              : t('resetDataFailureCopy')}
        </button>
      </div>
    </details>
  );
}
