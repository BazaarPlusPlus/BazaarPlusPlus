import { ChevronDown } from 'lucide-react';
import { useI18n } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import type {
  HistoryKeyCard,
  HistoryPeriod,
  HistoryResultFilter,
  HistoryRunFilter
} from '../../types/backend';
import { formatHero } from './format';
import { HISTORY_PERIODS, HISTORY_RESULTS } from './pagination';

const PERIOD_LABELS: Record<HistoryPeriod, MessageKey> = {
  all: 'historyPeriodAll',
  today: 'historyPeriodToday',
  last_7_days: 'historyPeriodLast7Days',
  last_30_days: 'historyPeriodLast30Days'
};

const RESULT_LABELS: Record<HistoryResultFilter, MessageKey> = {
  all: 'historyResultAll',
  win: 'historyResultWin',
  loss: 'historyResultLoss'
};

const ALL = '';

export function HistoryFilters({
  filter,
  heroes,
  keyCards,
  onChange
}: {
  filter: HistoryRunFilter;
  heroes: string[];
  keyCards: HistoryKeyCard[];
  onChange: (filter: HistoryRunFilter) => void;
}) {
  const { locale, t } = useI18n();
  // A hero from the URL stays selectable even when no Run has it.
  const heroOptions =
    filter.hero && !heroes.includes(filter.hero)
      ? [...heroes, filter.hero]
      : heroes;

  return (
    <div className="bpp-history-filters">
      <FilterSelect
        label={t('historyFilterHero')}
        value={filter.hero ?? ALL}
        options={[
          { value: ALL, label: t('historyFilterAllHeroes') },
          ...heroOptions.map((hero) => ({
            value: hero,
            label: formatHero(hero, t)
          }))
        ]}
        onChange={(hero) =>
          onChange({ ...filter, hero: hero === ALL ? null : hero })
        }
      />
      <FilterSelect
        label={t('historyFilterPeriod')}
        value={filter.period}
        options={HISTORY_PERIODS.map((period) => ({
          value: period,
          label: t(PERIOD_LABELS[period])
        }))}
        onChange={(period) =>
          onChange({ ...filter, period: period as HistoryPeriod })
        }
      />
      <FilterSelect
        label={t('historyFilterResult')}
        value={filter.result}
        options={HISTORY_RESULTS.map((result) => ({
          value: result,
          label: t(RESULT_LABELS[result])
        }))}
        onChange={(result) =>
          onChange({ ...filter, result: result as HistoryResultFilter })
        }
      />
      <FilterSelect
        label={t('historyFilterKeyCard')}
        value={filter.key_card ?? ALL}
        options={[
          { value: ALL, label: t('historyFilterAllKeyCards') },
          ...keyCards.map((card) => ({
            value: card.template_id,
            label: (locale === 'zh' && card.name_zh) || card.name
          }))
        ]}
        onChange={(keyCard) =>
          onChange({ ...filter, key_card: keyCard === ALL ? null : keyCard })
        }
      />
    </div>
  );
}

function FilterSelect({
  label,
  value,
  options,
  onChange
}: {
  label: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
}) {
  return (
    <div className="bpp-select-wrap">
      <select
        className="bpp-select"
        aria-label={label}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <ChevronDown size={14} aria-hidden="true" />
    </div>
  );
}
