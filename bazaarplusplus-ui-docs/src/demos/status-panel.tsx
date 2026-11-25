import { StatusPanel } from '@bpp/ui';
import { translate, type DemoProps } from '../copy';

export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <div className="grid gap-3">
      <StatusPanel kind="loading" compact title={t('读取中', 'Loading')} />
      <StatusPanel kind="empty" title={t('暂无战绩', 'No runs yet')} />
    </div>
  );
}
