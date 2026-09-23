import { DownloadCloud, Folder, Play, RefreshCw } from 'lucide-react';
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
  const explainDisabled = primary.disabled && !primary.running;

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
      variant="primary"
      disabled={primary.disabled}
      busy={primary.running}
      icon={<Icon aria-hidden="true" />}
      aria-describedby={
        explainDisabled && descriptionId ? descriptionId : undefined
      }
      title={explainDisabled ? descriptionText : undefined}
      onClick={onClick}
    >
      {label}
    </Button>
  );
}
