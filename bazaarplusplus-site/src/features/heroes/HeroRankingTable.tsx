import { Fragment, useMemo, useState, type CSSProperties, type ReactNode } from 'react';

import type { Locale } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import { formatInteger, formatNullablePercent } from '../../shared/lib/dashboard';
import { getHeroColor } from '../../shared/lib/heroes';
import HeroBadge from '../../shared/components/HeroBadge';
import type { HeroRanking } from './hero-analysis';

type RankingSortKey =
  | 'hero'
  | 'tenWinRate'
  | 'runsCompleted'
  | 'runShare'
  | 'tenWinCount'
  | 'avgRunDays10w'
  | 'perfectRate'
  | 'goldRate'
  | 'silverRate'
  | 'bronzeRate'
  | 'misfortuneRate';

type SortDirection = 'asc' | 'desc';
type SortValue = string | number | null;
type TableHeaders = ReturnType<typeof getSiteCopy>['stats']['heroes']['tableHeaders'];

type ColumnContext = {
  locale: Locale;
  focusedHero: string | null;
  maxTenWinRate: number;
  noValueLabel: string;
  onFocusHero: (hero: string) => void;
};

type RankingColumn = {
  key: RankingSortKey;
  label: keyof TableHeaders;
  width: string;
  initialDirection: SortDirection;
  headerClassName: string;
  value: (row: HeroRanking) => SortValue;
  renderCell: (row: HeroRanking, context: ColumnContext) => ReactNode;
};

function renderRate(value: number | null, noValueLabel: string) {
  return (
    <span className="relative tnum" aria-label={value == null ? noValueLabel : undefined}>
      {formatNullablePercent(value)}
    </span>
  );
}

function formatDays(value: number | null): string {
  return value == null ? '—' : value.toFixed(1);
}

const RANKING_COLUMNS: RankingColumn[] = [
  {
    key: 'hero',
    label: 'hero',
    width: '104px',
    initialDirection: 'asc',
    headerClassName:
      'sticky left-0 z-20 border-r border-[color:var(--color-border-soft)] bg-[color:var(--color-bg-card)] px-5 py-3.5',
    value: (row) => row.hero,
    renderCell: (row, context) => {
      const selected = row.hero === context.focusedHero;
      return (
        <td className="sticky left-0 z-10 border-r border-[color:var(--color-border-soft)] bg-[color:var(--color-bg-card)] px-5 py-4">
          <button
            type="button"
            data-selected={selected ? 'true' : 'false'}
            onClick={() => context.onFocusHero(row.hero)}
            className="inline-flex items-center bg-transparent p-0 text-left transition"
          >
            <HeroBadge hero={row.hero} selected={selected} size="sm" />
          </button>
        </td>
      );
    },
  },
  {
    key: 'tenWinRate',
    label: 'winRate',
    width: '12.5%',
    initialDirection: 'desc',
    headerClassName: 'px-5 py-3.5',
    value: (row) => row.tenWinRate,
    renderCell: (row, context) => {
      const rateRatio =
        row.tenWinRate != null && context.maxTenWinRate > 0
          ? row.tenWinRate / context.maxTenWinRate
          : 0;
      return (
        <td
          className="databar relative px-5 py-4 tnum text-[color:var(--color-accent-bright)]"
          style={{ '--bar-width': `${rateRatio * 100}%` } as CSSProperties}
        >
          {renderRate(row.tenWinRate, context.noValueLabel)}
        </td>
      );
    },
  },
  {
    key: 'runsCompleted',
    label: 'runs',
    width: '11%',
    initialDirection: 'desc',
    headerClassName: 'px-3 py-3.5',
    value: (row) => row.runsCompleted,
    renderCell: (row, context) => (
      <td className="px-3 py-4 tnum text-[color:var(--color-text-muted)]">
        {formatInteger(row.runsCompleted, context.locale)}
      </td>
    ),
  },
  {
    key: 'runShare',
    label: 'runShare',
    width: '11%',
    initialDirection: 'desc',
    headerClassName: 'px-3 py-3.5',
    value: (row) => row.runShare,
    renderCell: (row, context) => (
      <td className="px-3 py-4 tnum text-[color:var(--color-text-muted)]">
        {renderRate(row.runShare, context.noValueLabel)}
      </td>
    ),
  },
  {
    key: 'tenWinCount',
    label: 'wins10w',
    width: '10%',
    initialDirection: 'desc',
    headerClassName: 'px-3 py-3.5',
    value: (row) => row.tenWinCount,
    renderCell: (row, context) => (
      <td className="px-3 py-4 tnum text-[color:var(--color-text-base)]">
        {formatInteger(row.tenWinCount, context.locale)}
      </td>
    ),
  },
  {
    key: 'avgRunDays10w',
    label: 'avgDays',
    width: '11%',
    initialDirection: 'asc',
    headerClassName: 'px-3 py-3.5',
    value: (row) => row.avgRunDays10w,
    renderCell: (row, context) => (
      <td
        className="px-3 py-4 tnum text-[color:var(--color-text-muted)]"
        aria-label={row.avgRunDays10w == null ? context.noValueLabel : undefined}
      >
        {formatDays(row.avgRunDays10w)}
      </td>
    ),
  },
  ...(
    [
      ['perfectRate', 'perfect', '8.7%'],
      ['goldRate', 'gold', '8.7%'],
      ['silverRate', 'silver', '8.7%'],
      ['bronzeRate', 'bronze', '8.7%'],
      ['misfortuneRate', 'misfortune', '9.7%'],
    ] as const
  ).map(([key, label, width]): RankingColumn => ({
    key,
    label,
    width,
    initialDirection: 'desc',
    headerClassName: 'px-3 py-3.5',
    value: (row) => row[key],
    renderCell: (row, context) => (
      <td className="px-3 py-4 tnum">{renderRate(row[key], context.noValueLabel)}</td>
    ),
  })),
];

function sortRanking(
  rows: HeroRanking[],
  key: RankingSortKey,
  direction: SortDirection
): HeroRanking[] {
  const column = RANKING_COLUMNS.find((candidate) => candidate.key === key)!;
  return [...rows].sort((left, right) => {
    const leftValue = column.value(left);
    const rightValue = column.value(right);
    if (leftValue == null || rightValue == null) {
      if (leftValue == null && rightValue == null) {
        return 0;
      }
      return leftValue == null ? 1 : -1;
    }

    const result =
      typeof leftValue === 'number' && typeof rightValue === 'number'
        ? leftValue - rightValue
        : String(leftValue).localeCompare(String(rightValue), undefined, {
            numeric: true,
            sensitivity: 'base',
          });
    return direction === 'asc' ? result : -result;
  });
}

function SortableHeader({
  column,
  label,
  activeKey,
  direction,
  onSort,
}: {
  column: RankingColumn;
  label: string;
  activeKey: RankingSortKey;
  direction: SortDirection;
  onSort: (column: RankingColumn) => void;
}) {
  const activeDirection = activeKey === column.key ? direction : undefined;
  const isAsc = activeDirection === 'asc';
  const isDesc = activeDirection === 'desc';
  const isActive = isAsc || isDesc;

  return (
    <th
      scope="col"
      aria-sort={isAsc ? 'ascending' : isDesc ? 'descending' : 'none'}
      className={column.headerClassName}
    >
      <button
        type="button"
        onClick={() => onSort(column)}
        className={`group inline-flex min-w-max items-center gap-1.5 whitespace-nowrap text-left transition ${
          isActive
            ? 'text-[color:var(--color-accent-bright)]'
            : 'text-[color:var(--color-text-muted)] hover:text-[color:var(--color-text-base)]'
        }`}
      >
        <span className="whitespace-nowrap">{label}</span>
        <span
          aria-hidden="true"
          className={`flex flex-col leading-[0.6] text-[0.6rem] transition ${
            isActive ? 'text-[color:var(--color-accent)]' : 'text-[color:var(--color-text-faint)]'
          }`}
        >
          <span
            className={`transition ${
              isAsc
                ? 'text-[color:var(--color-accent-bright)] opacity-100'
                : isDesc
                  ? 'opacity-30'
                  : 'opacity-50'
            }`}
          >
            ▲
          </span>
          <span
            className={`transition ${
              isDesc
                ? 'text-[color:var(--color-accent-bright)] opacity-100'
                : isAsc
                  ? 'opacity-30'
                  : 'opacity-50'
            }`}
          >
            ▼
          </span>
        </span>
      </button>
    </th>
  );
}

export default function HeroRankingTable({
  locale,
  rows,
  focusedHero,
  onFocusHero,
}: {
  locale: Locale;
  rows: HeroRanking[];
  focusedHero: string | null;
  onFocusHero: (hero: string) => void;
}) {
  const copy = getSiteCopy(locale);
  const [sort, setSort] = useState<{
    key: RankingSortKey;
    direction: SortDirection;
  }>({ key: 'tenWinRate', direction: 'desc' });
  const sortedRows = useMemo(
    () => sortRanking(rows, sort.key, sort.direction),
    [rows, sort.direction, sort.key]
  );
  const maxTenWinRate = rows.reduce(
    (max, row) => (row.tenWinRate != null && row.tenWinRate > max ? row.tenWinRate : max),
    0
  );
  const context: ColumnContext = {
    locale,
    focusedHero,
    maxTenWinRate,
    noValueLabel: copy.common.noValueLabel,
    onFocusHero,
  };

  const changeSort = (column: RankingColumn) => {
    setSort((current) =>
      current.key === column.key
        ? {
            key: current.key,
            direction: current.direction === 'desc' ? 'asc' : 'desc',
          }
        : { key: column.key, direction: column.initialDirection }
    );
  };

  return (
    <div className="overflow-x-auto">
      <table
        data-testid="hero-ranking-table"
        className="w-full min-w-[840px] table-fixed border-collapse"
      >
        <colgroup>
          {RANKING_COLUMNS.map((column) => (
            <col key={column.key} style={{ width: column.width }} />
          ))}
        </colgroup>
        <thead className="text-left text-[0.72rem] font-medium uppercase tracking-[0.14em] text-[color:var(--color-text-muted)]">
          <tr>
            {RANKING_COLUMNS.map((column) => (
              <SortableHeader
                key={column.key}
                column={column}
                label={copy.stats.heroes.tableHeaders[column.label]}
                activeKey={sort.key}
                direction={sort.direction}
                onSort={changeSort}
              />
            ))}
          </tr>
        </thead>
        <tbody>
          {sortedRows.map((row) => (
            <tr
              key={row.hero}
              className="metric-row hero-rail border-t border-[color:var(--color-border-soft)] text-sm text-[color:var(--color-text-base)]"
              style={{ '--hero-color': getHeroColor(row.hero) } as CSSProperties}
            >
              {RANKING_COLUMNS.map((column) => (
                <Fragment key={column.key}>{column.renderCell(row, context)}</Fragment>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
