import { DownloadCloud, Folder, Loader2, Play, RefreshCw } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { useI18n } from '../../i18n/LocaleProvider';
import type {
  InstallPageSnapshot,
  InstallWorkflowIntents
} from './installWorkflow';

export function PrimaryInstallActionButton({
  snapshot,
  intents,
  descriptionId,
  descriptionText
}: {
  snapshot: Extract<InstallPageSnapshot, { phase: 'ready' }>;
  intents: InstallWorkflowIntents;
  descriptionId?: string;
  descriptionText?: string;
}) {
  const { t } = useI18n();
  const primary = snapshot.primaryAction;
  const mode = primary.mode;
  const isLaunch = mode === 'launch';
  const isChoose = mode === 'choose-directory';
  const isRepair = mode === 'repair';
  const label = isChoose
    ? t('actionChooseDirectory')
    : isLaunch
      ? t('launchGame')
      : isRepair
        ? t('actionReinstall')
        : t('actionInstall');
  const Icon = isChoose
    ? Folder
    : isLaunch
      ? Play
      : isRepair
        ? RefreshCw
        : DownloadCloud;
  const busy = primary.running;
  const explainDisabled = primary.disabled && !busy;

  const onClick = () => {
    if (isChoose) {
      void intents.chooseDirectory();
      return;
    }
    if (isLaunch) {
      void intents.launch();
      return;
    }
    intents.requestInstall();
  };

  return (
    <Button
      type="button"
      variant="primary"
      size="large"
      disabled={primary.disabled}
      busy={busy}
      aria-describedby={
        explainDisabled && descriptionId ? descriptionId : undefined
      }
      title={explainDisabled ? descriptionText : undefined}
      onClick={onClick}
    >
      <span className="bpp-install-primary-content flex min-w-0 items-center justify-center gap-3.5">
        {busy ? (
          <Loader2 size={28} className="bpp-primary-action-icon animate-spin" />
        ) : (
          <Icon size={28} className="bpp-primary-action-icon" />
        )}
        <span className="bpp-primary-action-label text-[18px] font-semibold">
          {label}
        </span>
      </span>
    </Button>
  );
}
