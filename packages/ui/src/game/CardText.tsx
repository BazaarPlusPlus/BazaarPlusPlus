import { Fragment } from 'react';
import { tierClass } from './Card';
import type { CardTier } from './types';

type Segment =
  | { text: string }
  | { keyword: string; label: string }
  | { token: string; tiers?: Partial<Record<CardTier, number>>; unit?: string };

export type CardTextLine = {
  id: number;
  tiers: CardTier[];
  available: boolean;
  resolved: 'full' | 'partial' | 'none';
  segments: Segment[];
};

export function CardText({
  line,
  tier,
  unavailable
}: {
  line: CardTextLine;
  tier?: CardTier | null;
  unavailable: string;
}) {
  const missing = <span className="bpp-card-unavailable">{unavailable}</span>;
  if (!line.available) return missing;
  return (
    <p className="bpp-effect-line">
      {line.segments.map((segment, i) => {
        // Published line segments are immutable. Their position distinguishes repeated tokens.
        const key = `${line.id}-${i}`;
        if ('text' in segment) {
          const previous = line.segments[i - 1];
          return previous && 'token' in previous
            ? segment.text.replace(/^[%％]/, '')
            : segment.text;
        }
        if ('label' in segment)
          return (
            <span
              key={key}
              className={`bpp-keyword bpp-keyword-${segment.keyword.toLowerCase().replaceAll(' ', '-')}`}
            >
              {segment.label}
            </span>
          );
        if (!segment.tiers || line.resolved === 'none')
          return <Fragment key={key}>{missing}</Fragment>;
        const next = line.segments[i + 1];
        const percent =
          next && 'text' in next ? next.text.match(/^[%％]/)?.[0] : undefined;
        const values = line.tiers.map((quality) => segment.tiers?.[quality]);
        // A value every tier shares reads once instead of repeating along the ladder.
        if (
          values.every((value) => value !== undefined && value === values[0])
        ) {
          const Value = tier ? 'strong' : 'span';
          return (
            <Value key={key} className={`bpp-effect-value ${tierClass(tier)}`}>
              {values[0]}
              {percent}
            </Value>
          );
        }
        return (
          <Fragment key={key}>
            {line.tiers.map((quality, j) => {
              const value = segment.tiers?.[quality];
              const Value = tier === quality ? 'strong' : 'span';
              return (
                <Fragment key={quality}>
                  {j > 0 && <span className="bpp-effect-step"> » </span>}
                  {value === undefined ? (
                    missing
                  ) : (
                    <Value
                      className={`bpp-effect-value bpp-tier-${quality.toLowerCase()}`}
                    >
                      {value}
                      {percent}
                    </Value>
                  )}
                </Fragment>
              );
            })}
          </Fragment>
        );
      })}
    </p>
  );
}
