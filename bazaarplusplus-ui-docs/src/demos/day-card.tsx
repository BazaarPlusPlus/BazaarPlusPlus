import { DayCard, fillTerm, gameTerms } from '@bazaarplusplus/ui/game';
import type { DemoProps } from '../copy';
import run from '../demo-data/run.json';
export default function Day({ lang }: DemoProps) {
  return (
    <ol>
      <DayCard
        title={fillTerm(gameTerms[lang].day, { n: run.day })}
        battles={<p>{lang === 'zh' ? '当天的战斗' : 'Battles of the day'}</p>}
      />
    </ol>
  );
}
