import { Button } from '@bazaarplusplus/ui';
import { translate, type DemoProps } from '../copy';

export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <div className="flex flex-wrap gap-3">
      <Button variant="primary">{t('安装', 'Install')}</Button>
      <Button>{t('取消', 'Cancel')}</Button>
      <Button variant="danger">{t('卸载', 'Uninstall')}</Button>
      <Button busy busyLabel={t('安装中', 'Installing')}>
        {t('安装', 'Install')}
      </Button>
    </div>
  );
}
