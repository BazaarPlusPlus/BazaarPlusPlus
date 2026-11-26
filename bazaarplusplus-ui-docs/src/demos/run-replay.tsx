import { MomentPanel, RunReplay } from '@bpp/ui';
import { type DemoProps, translate } from '../copy';
import { card, pinned } from '../cards';
import run from '../demo-data/run.json';
export default function Replay({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const lookup = (id: string) =>
    card(
      lang,
      pinned.find((p) => p.id === id)!
    );
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
  const visits = [
    {
      key: run.hour,
      day: t('第 ' + run.day + ' 天', 'Day ' + run.day),
      title: t('商店', 'Shop'),
      hour: t('第 ' + run.hour + ' 小时', 'Hour ' + run.hour),
      gold: null,
      health: null,
      gap: null,
      steps: run.moments.map((m) => ({
        verb: t('买入', 'Buy'),
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
      previous={t('上一步', 'Previous')}
      next={t('下一步', 'Next')}
      render={(_, s) => {
        const m = run.moments[s];
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
    />
  );
}
