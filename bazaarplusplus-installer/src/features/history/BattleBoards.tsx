import { useEffect, useState } from 'react';
import { ChevronRight } from 'lucide-react';
import {
  Board,
  SkillStrip,
  cardTiers,
  enchantments,
  type CardSize
} from '@bpp/ui';
import { commandClient } from '../../api/commandClient';
import { useI18n } from '../../i18n/LocaleProvider';
import type { HistoryBattleCard, HistoryBattleRow } from '../../types/backend';

type ArtIndex = Record<string, string | undefined>;

export function useBattleArt(enabled: boolean) {
  const [index, setIndex] = useState<ArtIndex>({});
  useEffect(() => {
    if (!enabled) return;
    let active = true;
    void commandClient
      .getCardArtIndex()
      .then((files) => {
        if (active) setIndex(files);
      })
      .catch(() => {
        /* Board tiles already display their local names. */
      });
    return () => {
      active = false;
    };
  }, [enabled]);
  return index;
}

export function BattleBoards({
  battle,
  art
}: {
  battle: HistoryBattleRow;
  art: ArtIndex;
}) {
  const { locale, t } = useI18n();
  function tile(card: HistoryBattleCard) {
    const file = art[card.template_id.toLowerCase()];
    const tier = cardTiers.find((value) => value === card.tier) ?? null;
    const enchant =
      enchantments.find((value) => value === card.enchant) ?? null;
    const size: CardSize = card.size === 3 || card.size === 2 ? card.size : 1;
    return {
      id: card.instance_id,
      name: locale === 'zh' ? (card.name_zh ?? card.name) : card.name,
      art:
        file && /^[0-9a-f]{8,64}\.webp$/.test(file)
          ? 'https://cdn.bazaarplusplus.com/card-art/' + file
          : null,
      tier,
      tierLabel: tier ? t(`rank${tier}`) : (card.tier ?? ''),
      enchant,
      enchantLabel: enchant ? t(`enchant${enchant}`) : (card.enchant ?? ''),
      size,
      slot: card.slot
    };
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
          label={t('battleSkills')}
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
