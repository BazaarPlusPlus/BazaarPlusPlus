import { PageHeader, Badge } from '@bpp/ui';
import { translate, type DemoProps } from '../copy';

export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <PageHeader
      title={t('战绩', 'History')}
      meta={<Badge dot={false}>{t('本地', 'Local')}</Badge>}
    />
  );
}
