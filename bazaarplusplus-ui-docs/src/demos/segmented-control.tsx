import { SegmentedControl } from '@bazaarplusplus/ui';
import { translate, type DemoProps } from '../copy';
import { useState } from 'react';
export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const [value, setValue] = useState('day');
  return (
    <SegmentedControl
      name="period"
      label={t('时间范围', 'Period')}
      options={[
        { value: 'day', label: t('今天', 'Today') },
        { value: 'week', label: t('本周', 'This week') }
      ]}
      value={value}
      onChange={setValue}
    />
  );
}
