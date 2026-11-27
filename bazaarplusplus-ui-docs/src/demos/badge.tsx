import { Badge } from '@bazaarplusplus/ui';
import { translate, type DemoProps } from '../copy';

export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <div className="flex flex-wrap gap-3">
      <Badge tone="ok">{t('已安装', 'Installed')}</Badge>
      <Badge tone="warn">{t('有更新', 'Update available')}</Badge>
      <Badge tone="bad">{t('失败', 'Failed')}</Badge>
      <Badge dot={false}>{t('本地', 'Local')}</Badge>
    </div>
  );
}
