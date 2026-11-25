import tokens from '../../design/tokens.json';
import { type DemoProps, translate } from './copy';
export default function Foundations({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const groups = [
    [
      t('颜色', 'Colour'),
      tokens.color.tokens.map((c) => ({
        name: c.name,
        value: c.value.dark,
        swatch: c.value.dark
      }))
    ],
    [t('间距', 'Spacing'), tokens.spacing.tokens],
    [t('圆角', 'Radius'), tokens.radius.tokens],
    [
      t('字号', 'Type'),
      tokens.type.groups.flatMap((g) =>
        g.styles.map((s) => ({ name: s.name, value: s.fontSize }))
      )
    ],
    [t('动效', 'Motion'), tokens.motion.tokens]
  ] as const;
  return groups.map(([title, entries]) => (
    <section key={title}>
      <h2 className="text-title mb-3">{title}</h2>
      <dl className="grid sm:grid-cols-2 gap-x-6">
        {entries.map((e) => (
          <div
            key={e.name}
            className="flex items-center gap-2 border-t border-line py-2 text-code font-mono"
          >
            {'swatch' in e && (
              <span
                className="size-4 rounded-inner"
                style={{ background: e.swatch }}
              />
            )}
            <dt className="break-all">{e.name}</dt>
            <dd className="ml-auto text-text-2">{e.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  ));
}
