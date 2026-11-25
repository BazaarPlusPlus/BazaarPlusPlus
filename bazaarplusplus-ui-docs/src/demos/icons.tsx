import {
  AlertIcon,
  CloseIcon,
  CompassIcon,
  ErrorIcon,
  InboxIcon,
  InfoIcon,
  SpinnerIcon,
  SuccessIcon,
  WarningIcon
} from '@bpp/ui';
import { translate, type DemoProps } from '../copy';

export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <div aria-label={t('图标', 'Icons')} className="flex flex-wrap gap-4">
      <AlertIcon />
      <CloseIcon />
      <CompassIcon />
      <ErrorIcon />
      <InboxIcon />
      <InfoIcon />
      <SpinnerIcon />
      <SuccessIcon />
      <WarningIcon />
    </div>
  );
}
