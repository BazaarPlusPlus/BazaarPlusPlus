import { Fragment, useMemo, useState, type ReactNode } from 'react';

import type { Locale } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import { formatInteger, formatNullablePercent } from '../../shared/lib/dashboard';
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

const CELL = 'h-11 px-3 text-right tabular-nums';
const HEADER_CELL = 'px-3 py-2.5 text-right';

function renderRate(value: number | null, noValueLabel: string) {
  return (
    <span aria-label={value == null ? noValueLabel : undefined}>
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
    headerClassName: 'sticky left-0 z-20 border-r border-line bg-panel px-4 py-2.5 text-left',
    value: (row) => row.hero,
    renderCell: (row, context) => {
      const selected = row.hero === context.focusedHero;
      return (
        <td className="sticky left-0 z-10 h-11 border-r border-line bg-panel px-4 transition-colors duration-(--t-fast) group-hover:bg-hover">
          <button
            type="button"
            data-selected={selected ? 'true' : 'false'}
            onClick={() => context.onFocusHero(row.hero)}
            className="inline-flex cursor-pointer items-center rounded-full bg-transparent p-0 text-left"
          >
            <HeroBadge hero={row.hero} selected={selected} />
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
    headerClassName: HEADER_CELL,
    value: (row) => row.tenWinRate,
    renderCell: (row, context) => {
      const rateRatio =
        row.tenWinRate != null && context.maxTenWinRate > 0
          ? row.tenWinRate / context.maxTenWinRate
          : 0;
      return (
        <td className={CELL}>
          <span className="inline-flex items-center justify-end gap-2.5">
            <span className="font-medium text-text-1">
              {renderRate(row.tenWinRate, context.noValueLabel)}
            </span>
            <span
              aria-hidden="true"
              className="hidden h-1.5 w-10 overflow-hidden rounded-full bg-hover sm:block"
            >
              <span
                className="block h-full rounded-full bg-accent"
                style={{ width: `${rateRatio * 100}%` }}
              />
            </span>
          </span>
        </td>
      );
    },
  },
  {
    key: 'runsCompleted',
    label: 'runs',
    width: '11%',
    initialDirection: 'desc',
    headerClassName: HEADER_CELL,
    value: (row) => row.runsCompleted,
    renderCell: (row, context) => (
      <td className={`${CELL} text-text-2`}>{formatInteger(row.runsCompleted, context.locale)}</td>
    ),
  },
  {
    key: 'runShare',
    label: 'runShare',
    width: '11%',
    initialDirection: 'desc',
    headerClassName: HEADER_CELL,
    value: (row) => row.runShare,
    renderCell: (row, context) => (
      <td className={`${CELL} text-text-2`}>{renderRate(row.runShare, context.noValueLabel)}</td>
    ),
  },
  {
    key: 'tenWinCount',
    label: 'wins10w',
    width: '10%',
    initialDirection: 'desc',
    headerClassName: HEADER_CELL,
    value: (row) => row.tenWinCount,
    renderCell: (row, context) => (
      <td className={`${CELL} text-text-1`}>{formatInteger(row.tenWinCount, context.locale)}</td>
    ),
  },
  {
    key: 'avgRunDays10w',
    label: 'avgDays',
    width: '11%',
    initialDirection: 'asc',
    headerClassName: HEADER_CELL,
    value: (row) => row.avgRunDays10w,
    renderCell: (row, context) => (
      <td
        className={`${CELL} text-text-2`}
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
    headerClassName: HEADER_CELL,
    value: (row) => row[key],
    renderCell: (row, context) => (
      <td className={`${CELL} text-text-2`}>{renderRate(row[key], context.noValueLabel)}</td>
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
        className={`inline-flex min-w-max cursor-pointer items-center gap-1.5 rounded-[4px] whitespace-nowrap transition-colors duration-(--t-fast) ${
          isActive ? 'text-text-1' : 'text-text-3 hover:text-text-1'
        }`}
      >
        <span className="whitespace-nowrap">{label}</span>
        <span aria-hidden="true" className="flex flex-col text-[0.55rem] leading-[0.7]">
          <span className={isAsc ? 'text-accent' : 'opacity-40'}>▲</span>
          <span className={isDesc ? 'text-accent' : 'opacity-40'}>▼</span>
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
        <thead className="text-xs font-medium text-text-3">
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
              className="group border-t border-line text-[13px] text-text-1 transition-colors duration-(--t-fast) hover:bg-hover"
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
