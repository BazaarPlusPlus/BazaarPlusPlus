import { ChevronRight } from 'lucide-react';
import { Board, SkillStrip, useCardCatalog } from '@bpp/ui';
import { useI18n } from '../../i18n/LocaleProvider';
import type { HistoryBattleCard, HistoryBattleRow } from '../../types/backend';

export function BattleBoards({ battle }: { battle: HistoryBattleRow }) {
  const { locale, t } = useI18n();
  const { card, terms } = useCardCatalog();
  function tile(c: HistoryBattleCard) {
    return {
      ...card({
        templateId: c.template_id,
        tier: c.tier,
        enchant: c.enchant,
        size: c.size,
        name: locale === 'zh' ? (c.name_zh ?? c.name) : c.name
      }),
      id: c.instance_id,
      slot: c.slot,
      size: c.size === 3 || c.size === 2 ? c.size : 1
    } as const;
  }
  function loadout(
    items: HistoryBattleCard[] | null,
    skills: HistoryBattleCard[] | null
  ) {
    return (
      <>
        <Board
          cards={items?.map(tile) ?? null}
          missingLabel={t('battleMissingBoard')}
          emptyLabel={t('battleEmptyBoard')}
        />
        <SkillStrip
          cards={skills?.map(tile) ?? null}
          missingLabel={t('battleMissingSkills')}
          emptyLabel={t('battleEmptySkills')}
          label={terms.skills}
        />
      </>
    );
  }
  return (
    <div className="flex flex-col gap-2 whitespace-normal">
      {loadout(battle.player_board, battle.player_skills)}
      <details className="bpp-details">
        <summary>
          <ChevronRight size={14} className="bpp-details-chevron" />
          {t('battleOpponentBoard')}
        </summary>
        <div className="mt-2 flex flex-col gap-2">
          {loadout(battle.opponent_board, battle.opponent_skills)}
        </div>
      </details>
    </div>
  );
}
