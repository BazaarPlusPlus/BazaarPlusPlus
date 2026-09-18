import {
  useState,
  type MouseEvent as ReactMouseEvent,
  type TouchEvent as ReactTouchEvent,
} from 'react';

import type { Locale } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import { formatNullablePercent, formatPercent, formatShortDate } from '../../shared/lib/dashboard';
import { getHeroShortLabel } from '../../shared/lib/heroes';
import type { HeroAnalysis } from './hero-analysis';

type HeroTrendPanelProps = {
  locale: Locale;
  trend: HeroAnalysis['trend'];
  focusedHero: string | null;
  onFocusHero: (hero: string) => void;
};

type ChartPoint = {
  day: string;
  winRate: number;
  x: number;
  y: number;
};

type HoveredTrendPoint = ChartPoint & {
  hero: string;
  color: string;
};

const CHART_WIDTH = 960;
const CHART_HEIGHT = 360;
const CHART_PADDING = { top: 28, right: 28, bottom: 48, left: 56 };
const POINT_TOOLTIP_WIDTH = 168;
const POINT_TOOLTIP_HEIGHT = 56;
const POINT_TOOLTIP_OFFSET = 18;
const GRIDLINE_COUNT = 4;

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function getChartX(dayIndex: number, dayCount: number) {
  const chartInnerWidth = CHART_WIDTH - CHART_PADDING.left - CHART_PADDING.right;
  if (dayCount <= 1) {
    return CHART_PADDING.left + chartInnerWidth / 2;
  }

  return CHART_PADDING.left + (chartInnerWidth * dayIndex) / (dayCount - 1);
}

function getChartY(value: number, min: number, max: number) {
  const chartInnerHeight = CHART_HEIGHT - CHART_PADDING.top - CHART_PADDING.bottom;
  if (max - min <= 0.0001) {
    return CHART_PADDING.top + chartInnerHeight / 2;
  }

  const normalized = (value - min) / (max - min);
  return CHART_PADDING.top + chartInnerHeight * (1 - normalized);
}

function getNearestChartPoint(
  event: ReactMouseEvent<SVGElement> | ReactTouchEvent<SVGElement>,
  points: ChartPoint[]
): ChartPoint | undefined {
  if (points.length === 0) {
    return undefined;
  }

  const svg = event.currentTarget.ownerSVGElement || (event.currentTarget as SVGSVGElement);
  const rect = svg?.getBoundingClientRect();
  if (rect == null || rect.width <= 0) {
    return points.at(-1);
  }

  const clientX = 'touches' in event ? event.touches[0].clientX : event.clientX;
  const pointerX = ((clientX - rect.left) / rect.width) * CHART_WIDTH;
  return points.reduce((nearest, point) =>
    Math.abs(point.x - pointerX) < Math.abs(nearest.x - pointerX) ? point : nearest
  );
}

export default function HeroTrendPanel({
  locale,
  trend,
  focusedHero,
  onFocusHero,
}: HeroTrendPanelProps) {
  const heroCopy = getSiteCopy(locale).stats.heroes;
  const { dayAxis, series } = trend;
  const focusedSeries = series.find((heroSeries) => heroSeries.hero === focusedHero);
  const [hoveredPoint, setHoveredPoint] = useState<HoveredTrendPoint | null>(null);

  const winRates = series.flatMap((heroSeries) => heroSeries.points.map((point) => point.winRate));
  const minWinRate = winRates.length > 0 ? Math.min(...winRates) : 0;
  const maxWinRate = winRates.length > 0 ? Math.max(...winRates) : 1;
  const spread = Math.max(maxWinRate - minWinRate, 0.04);
  const paddedMin = clamp((Math.floor(((minWinRate - spread * 0.2) * 100) / 5) * 5) / 100, 0, 0.95);
  const paddedMax = clamp((Math.ceil(((maxWinRate + spread * 0.2) * 100) / 5) * 5) / 100, 0.05, 1);
  const yMin = Math.min(paddedMin, maxWinRate);
  const yMax = Math.max(paddedMax, minWinRate + 0.01);
  const tickStep = (yMax - yMin) / GRIDLINE_COUNT;
  const yAxisTicks = Array.from(
    { length: GRIDLINE_COUNT + 1 },
    (_, index) => yMin + tickStep * index
  );
  const tooltipX =
    hoveredPoint == null
      ? 0
      : clamp(
          hoveredPoint.x - POINT_TOOLTIP_WIDTH / 2,
          CHART_PADDING.left,
          CHART_WIDTH - CHART_PADDING.right - POINT_TOOLTIP_WIDTH
        );
  const tooltipY =
    hoveredPoint == null
      ? 0
      : clamp(
          hoveredPoint.y - POINT_TOOLTIP_HEIGHT - POINT_TOOLTIP_OFFSET,
          CHART_PADDING.top,
          CHART_HEIGHT - CHART_PADDING.bottom - POINT_TOOLTIP_HEIGHT
        );

  const toChartPoint = (point: { day: string; winRate: number }): ChartPoint => ({
    day: point.day,
    winRate: point.winRate,
    x: getChartX(dayAxis.indexOf(point.day), dayAxis.length),
    y: getChartY(point.winRate, yMin, yMax),
  });

  return (
    <section data-testid="trend-panel" className="grid min-w-0 gap-3">
      <div
        data-testid="daily-winrate-chart"
        className="relative h-full min-h-[220px] min-w-0 overflow-hidden rounded-2xl border border-[color:var(--color-border-soft)] bg-[linear-gradient(180deg,rgba(232,185,74,0.04),rgba(8,6,4,0.95))] sm:min-h-[280px] xl:min-h-[320px]"
      >
        <div className="h-full w-full overflow-x-auto overflow-y-hidden p-2 sm:p-3">
          {series.length > 0 ? (
            <svg
              viewBox={`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`}
              className="block h-full w-full min-w-[720px]"
              role="img"
              aria-label={heroCopy.trend.chartAriaLabel}
            >
              <defs>
                {focusedSeries ? (
                  <linearGradient id="focused-area" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={focusedSeries.color} stopOpacity="0.35" />
                    <stop offset="100%" stopColor={focusedSeries.color} stopOpacity="0" />
                  </linearGradient>
                ) : null}
              </defs>

              {yAxisTicks.map((tick) => {
                const y = getChartY(tick, yMin, yMax);
                return (
                  <g key={`tick-${tick.toFixed(4)}`}>
                    <line
                      x1={CHART_PADDING.left}
                      y1={y}
                      x2={CHART_WIDTH - CHART_PADDING.right}
                      y2={y}
                      stroke="rgba(148, 131, 95, 0.14)"
                      strokeWidth="1"
                      strokeDasharray="2 4"
                    />
                    <text
                      x={CHART_PADDING.left - 12}
                      y={y + 4}
                      fill="rgba(179, 160, 121, 0.95)"
                      fontSize="11"
                      fontFamily="JetBrains Mono, monospace"
                      textAnchor="end"
                    >
                      {formatPercent(tick)}
                    </text>
                  </g>
                );
              })}

              {dayAxis.map((day, index) => {
                const x = getChartX(index, dayAxis.length);
                const isLast = index === dayAxis.length - 1;
                return (
                  <g key={day}>
                    <line
                      x1={x}
                      y1={CHART_PADDING.top}
                      x2={x}
                      y2={CHART_HEIGHT - CHART_PADDING.bottom}
                      stroke="rgba(148, 131, 95, 0.06)"
                      strokeWidth="1"
                    />
                    <text
                      x={x}
                      y={CHART_HEIGHT - 14}
                      fill={isLast ? 'rgba(255,212,122,0.95)' : 'rgba(179,160,121,0.95)'}
                      fontSize="11"
                      fontFamily="JetBrains Mono, monospace"
                      fontWeight={isLast ? '600' : '400'}
                      textAnchor="middle"
                    >
                      {formatShortDate(day, locale)}
                    </text>
                  </g>
                );
              })}

              {focusedSeries
                ? focusedSeries.segments.map((segment, segmentIndex) => {
                    const points = segment.map(toChartPoint);
                    if (points.length === 0) {
                      return null;
                    }
                    const path = `M ${points[0].x} ${
                      CHART_HEIGHT - CHART_PADDING.bottom
                    } L ${points.map((point) => `${point.x} ${point.y}`).join(' L ')} L ${
                      points.at(-1)!.x
                    } ${CHART_HEIGHT - CHART_PADDING.bottom} Z`;
                    return (
                      <path
                        key={`area-${segmentIndex}`}
                        data-testid="focused-trend-area"
                        d={path}
                        fill="url(#focused-area)"
                      />
                    );
                  })
                : null}

              {series.map((heroSeries) => {
                const isFocused = heroSeries.hero === focusedSeries?.hero;
                const allPoints = heroSeries.points.map(toChartPoint);
                const segments = heroSeries.segments.map((segment) => segment.map(toChartPoint));
                const showPoint = (point: ChartPoint | undefined) => {
                  if (point == null) {
                    return;
                  }
                  onFocusHero(heroSeries.hero);
                  setHoveredPoint({ ...point, hero: heroSeries.hero, color: heroSeries.color });
                };
                const clearPoint = () => setHoveredPoint(null);

                return (
                  <g
                    key={heroSeries.hero}
                    data-testid="daily-winrate-line"
                    data-hero={heroSeries.hero}
                    data-selected={isFocused ? 'true' : 'false'}
                    className="transition-opacity duration-200"
                  >
                    {segments.map((segmentPoints, segmentIndex) => (
                      <polyline
                        key={`line-${segmentIndex}`}
                        fill="none"
                        stroke={heroSeries.color}
                        strokeOpacity={isFocused ? '1' : '0.18'}
                        strokeWidth={isFocused ? '3' : '2'}
                        strokeLinejoin="round"
                        strokeLinecap="round"
                        points={segmentPoints.map((point) => `${point.x},${point.y}`).join(' ')}
                        style={
                          isFocused
                            ? { filter: `drop-shadow(0 0 6px ${heroSeries.color}88)` }
                            : undefined
                        }
                      />
                    ))}
                    {segments.map((segmentPoints, segmentIndex) => (
                      <polyline
                        key={`hover-${segmentIndex}`}
                        fill="none"
                        stroke="transparent"
                        strokeWidth="22"
                        strokeLinejoin="round"
                        strokeLinecap="round"
                        points={segmentPoints.map((point) => `${point.x},${point.y}`).join(' ')}
                        style={{ pointerEvents: 'stroke', cursor: 'pointer' }}
                        aria-hidden="true"
                        onMouseEnter={(event) => showPoint(getNearestChartPoint(event, allPoints))}
                        onMouseMove={(event) => showPoint(getNearestChartPoint(event, allPoints))}
                        onTouchStart={(event) => showPoint(getNearestChartPoint(event, allPoints))}
                        onTouchMove={(event) => showPoint(getNearestChartPoint(event, allPoints))}
                        onMouseLeave={clearPoint}
                        onTouchEnd={clearPoint}
                        onTouchCancel={clearPoint}
                        onClick={() => onFocusHero(heroSeries.hero)}
                      />
                    ))}
                    {allPoints.map((point, index) => (
                      <circle
                        key={`${heroSeries.hero}-${index}`}
                        cx={point.x}
                        cy={point.y}
                        r={isFocused ? '4.5' : '3'}
                        fill={heroSeries.color}
                        fillOpacity={isFocused ? '1' : '0.4'}
                        stroke="rgba(12,10,7,0.95)"
                        strokeWidth="2"
                        onMouseEnter={() => showPoint(point)}
                        onMouseLeave={clearPoint}
                        onFocus={() => showPoint(point)}
                        onBlur={clearPoint}
                        tabIndex={0}
                        aria-label={`${heroSeries.hero} ${formatShortDate(
                          point.day,
                          locale
                        )} ${formatPercent(point.winRate)}`}
                      />
                    ))}
                  </g>
                );
              })}

              {hoveredPoint ? (
                <g data-testid="trend-point-tooltip" pointerEvents="none">
                  <line
                    x1={hoveredPoint.x}
                    y1={hoveredPoint.y}
                    x2={hoveredPoint.x}
                    y2={CHART_HEIGHT - CHART_PADDING.bottom}
                    stroke={hoveredPoint.color}
                    strokeOpacity="0.4"
                    strokeWidth="1"
                    strokeDasharray="2 3"
                  />
                  <circle
                    cx={hoveredPoint.x}
                    cy={hoveredPoint.y}
                    r="8"
                    fill="none"
                    stroke={hoveredPoint.color}
                    strokeWidth="2"
                    strokeOpacity="0.6"
                  />
                  <rect
                    data-testid="trend-tooltip-box"
                    x={tooltipX}
                    y={tooltipY}
                    width={POINT_TOOLTIP_WIDTH}
                    height={POINT_TOOLTIP_HEIGHT}
                    rx="10"
                    fill="rgba(15,12,8,0.97)"
                    stroke={hoveredPoint.color}
                    strokeOpacity="0.55"
                    strokeWidth="1"
                  />
                  <rect
                    x={tooltipX + 10}
                    y={tooltipY + 12}
                    width="3"
                    height="32"
                    fill={hoveredPoint.color}
                    rx="1.5"
                  />
                  <text
                    x={tooltipX + 22}
                    y={tooltipY + 22}
                    fill="rgba(241,230,205,0.96)"
                    fontSize="12"
                    fontWeight="600"
                    fontFamily="IBM Plex Sans, sans-serif"
                  >
                    {hoveredPoint.hero}
                  </text>
                  <text
                    x={tooltipX + 22}
                    y={tooltipY + 38}
                    fill="rgba(148,131,95,0.95)"
                    fontSize="10"
                    fontFamily="JetBrains Mono, monospace"
                    letterSpacing="0.06em"
                  >
                    {formatShortDate(hoveredPoint.day, locale)}
                  </text>
                  <text
                    x={tooltipX + POINT_TOOLTIP_WIDTH - 14}
                    y={tooltipY + 35}
                    fill="rgba(255,212,122,1)"
                    fontSize="18"
                    fontWeight="700"
                    fontFamily="JetBrains Mono, monospace"
                    textAnchor="end"
                  >
                    {formatPercent(hoveredPoint.winRate)}
                  </text>
                </g>
              ) : null}
            </svg>
          ) : (
            <div className="flex h-full min-h-[220px] items-center justify-center text-sm text-[color:var(--color-text-muted)]">
              {heroCopy.snapshot.noData}
            </div>
          )}
        </div>
      </div>

      {focusedSeries != null && focusedSeries.nullPointCount > 0 ? (
        <p className="text-[0.72rem] leading-5 text-[color:var(--color-text-faint)]">
          {heroCopy.coverage.noTrendValue}
        </p>
      ) : null}

      <div className="grid min-w-0 grid-cols-2 gap-1.5 sm:grid-cols-4 2xl:grid-cols-7">
        {series.map((heroSeries) => {
          const isFocused = heroSeries.hero === focusedSeries?.hero;
          return (
            <button
              key={heroSeries.hero}
              type="button"
              data-selected={isFocused ? 'true' : 'false'}
              onMouseEnter={() => onFocusHero(heroSeries.hero)}
              onFocus={() => onFocusHero(heroSeries.hero)}
              onClick={() => onFocusHero(heroSeries.hero)}
              className={`group inline-flex min-w-0 items-center justify-between gap-2 rounded-lg border px-2.5 py-2 text-left transition ${
                isFocused
                  ? 'border-[color:var(--color-accent)] bg-[color:rgba(232,185,74,0.1)]'
                  : 'border-[color:var(--color-border-soft)] bg-[color:rgba(15,12,8,0.6)] hover:border-[color:var(--color-accent-deep)]'
              }`}
              aria-label={`${heroSeries.hero} ${formatNullablePercent(heroSeries.latestWinRate)}`}
              title={heroSeries.hero}
            >
              <span className="flex min-w-0 items-center gap-2">
                <span
                  className="h-4 w-1 shrink-0 rounded-sm"
                  style={{
                    backgroundColor: heroSeries.color,
                    boxShadow: isFocused
                      ? `0 0 10px ${heroSeries.color}aa`
                      : `0 0 6px ${heroSeries.color}55`,
                  }}
                  aria-hidden="true"
                />
                <span className="font-mono text-[0.7rem] font-semibold uppercase tracking-[0.16em] text-[color:var(--color-text-base)]">
                  {getHeroShortLabel(heroSeries.hero)}
                </span>
              </span>
              <span
                className={`tnum text-[0.74rem] font-semibold ${
                  isFocused
                    ? 'text-[color:var(--color-accent-bright)]'
                    : 'text-[color:var(--color-text-muted)]'
                }`}
              >
                {formatNullablePercent(heroSeries.latestWinRate)}
              </span>
            </button>
          );
        })}
      </div>
    </section>
  );
}
