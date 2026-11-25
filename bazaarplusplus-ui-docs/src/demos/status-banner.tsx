import { StatusBanner } from '@bpp/ui';
import { translate, type DemoProps } from '../copy';

export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <div className="grid gap-3">
      <StatusBanner tone="success" message={t('已保存战绩。', 'Run saved.')} />
      <StatusBanner
        tone="warning"
        message={t('更新后重启游戏。', 'Restart the game after updating.')}
      />
    </div>
  );
}
