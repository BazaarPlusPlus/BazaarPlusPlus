import { DecisionExplorer, DecisionVisitRow, MomentPanel } from '@bpp/ui';
import { type DemoProps, translate } from '../copy';
import { card, pinned } from '../cards';
import run from '../demo-data/run.json';
export default function Explorer({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const lookup = (id: string) =>
    card(
      lang,
      pinned.find((p) => p.id === id)!
    );
  const rows = run.moments.map((m) => ({
    id: m.seq,
    verb: t('买入', 'Buy'),
    chip: { id: m.card, title: lookup(m.card).name, card: lookup(m.card) },
    amount: null,
    result: null,
    status: null
  }));
  const labels = {
    board: t('阵容', 'Board'),
    stash: t('储藏箱', 'Stash'),
    skills: t('技能', 'Skills'),
    unknown: t('未记录', 'Not recorded'),
    empty: t('空', 'Empty'),
    pickedFrom: t('可选物品', 'Offers'),
    chosen: t('已选', 'Chosen'),
    goldTotal: t('金币', 'Gold'),
    healthTotal: t('生命', 'Health'),
    level: t('等级', 'Level'),
    resultOffer: t('刷新结果', 'Rerolled offers')
  };
  return (
    <DecisionExplorer
      ids={rows.map((row) => row.id)}
      previous={t('上一步', 'Previous')}
      next={t('下一步', 'Next')}
      render={(seq) => {
        const m = run.moments.find((item) => item.seq === seq)!;
        return (
          <MomentPanel
            title={t('买入', 'Bought') + ' · ' + lookup(m.card).name}
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
    >
      <DecisionVisitRow
        view={{
          title: t('商店', 'Shop'),
          hour: t('第 1 小时', 'Hour 1'),
          gold: null,
          health: null,
          gap: null,
          rows,
          remaining: [],
          remainingLabel: ''
        }}
        outcome={null}
        renderResult={String}
      />
    </DecisionExplorer>
  );
}
