import { DayCard } from '@bpp/ui';
import type { DemoProps } from '../copy';
import run from '../demo-data/run.json';
export default function Day({ lang }: DemoProps) {
  return (
    <ol>
      <DayCard
        title={lang === 'zh' ? '第 ' + run.day + ' 天' : 'Day ' + run.day}
        battles={<p>{lang === 'zh' ? '当天的战斗' : 'Battles of the day'}</p>}
      />
    </ol>
  );
}
