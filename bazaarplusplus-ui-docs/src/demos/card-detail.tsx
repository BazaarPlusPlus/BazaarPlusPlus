import { card, detail, pinned } from '../cards';
import data from '../demo-data/definition.json';
import type { DemoProps } from '../copy';
export default function Detail({ lang }: DemoProps) {
  const first = pinned.find((p) => p.id === data.id) ?? pinned[0];
  return detail(lang, card(lang, first), first.id, false);
}
