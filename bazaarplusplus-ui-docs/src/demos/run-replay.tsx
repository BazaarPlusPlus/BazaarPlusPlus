import {
  MomentPanel,
  RunReplay,
  fillTerm,
  gameTerms
} from '@bazaarplusplus/ui/game';
import type { DemoProps } from '../copy';
import { card, pinned } from '../cards';
import run from '../demo-data/run.json';
export default function Replay({ lang }: DemoProps) {
  const terms = gameTerms[lang];
  const labels = { ...terms.replay, skills: terms.skills };
  const lookup = (id: string) =>
    card(
      lang,
      pinned.find((p) => p.id === id)!
    );
  const visits = [
    {
      key: run.hour,
      day: fillTerm(terms.day, { n: run.day }),
      title: terms.replay.kinds.shop,
      hour: fillTerm(terms.replay.hour, { n: run.hour }),
      gold: null,
      health: null,
      gap: null,
      steps: run.moments.map((m) => ({
        verb: terms.replay.verbs.bought,
        card: { card: lookup(m.card), title: lookup(m.card).name },
        result: null,
        status: null,
        amount: null
      }))
    }
  ];
  return (
    <RunReplay
      visits={visits}
      previous={terms.replay.previous}
      next={terms.replay.next}
      render={(_, s) => {
        const m = run.moments[s];
        return (
          <MomentPanel
            title={terms.replay.verbs.bought + ' · ' + lookup(m.card).name}
            labels={labels}
            reroll={false}
            formatPrice={String}
            view={{
              ...m.snapshot,
              board: m.snapshot.board.map((c) =>
                Object.assign(lookup(c.templateId), {
                  slot: c.slot,
                  id: c.instanceId
                })
              ),
              encounter: null,
              encounterId: null,
              offer: undefined
            }}
          />
        );
      }}
    />
  );
}
