import { DayCard } from '@bpp/ui';
import Explorer from './decision-explorer';
import type { DemoProps } from '../copy';
import run from '../demo-data/run.json';
export default function Day({ lang }: DemoProps) {
  return (
    <ol>
      <DayCard
        title={lang === 'zh' ? '第 ' + run.day + ' 天' : 'Day ' + run.day}
        battles={null}
        decisionsLabel={lang === 'zh' ? '当天决策' : 'Decisions'}
        decisions={<Explorer lang={lang} />}
      />
    </ol>
  );
}
