import { useEffect, useRef } from 'react';
import { hasProgram, type CardFx } from './program';
import type { Enchantment } from '../types';
import { observeFx } from './renderer';

export function FxFace({
  source,
  enchant,
  still = false
}: {
  source: CardFx;
  enchant: Enchantment;
  still?: boolean;
}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    if (canvas.current)
      return observeFx(canvas.current, source, enchant, still);
  }, [source, enchant, still]);
  if (!hasProgram(enchant)) return null;
  return (
    <canvas
      ref={canvas}
      width={0}
      height={0}
      aria-hidden="true"
      className="bpp-card-fx"
    />
  );
}
