import InfoPageShell from '../../shared/components/InfoPageShell';
import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type TutorialPageCopy } from '../../content/site-copy';

type TutorialPageProps = {
  location: ResolvedSpaLocation;
};

function FeatureCard({
  feature,
  index,
}: {
  feature: TutorialPageCopy['features'][number];
  index: number;
}) {
  return (
    <article className="surface group flex flex-col gap-4 overflow-hidden p-5 transition duration-200 hover:border-[color:var(--color-border-bright)] sm:p-6">
      <span
        aria-hidden="true"
        className="absolute inset-x-0 top-0 h-px bg-[linear-gradient(90deg,rgba(232,185,74,0.72),transparent)] opacity-60"
      />
      <div className="flex items-start justify-between gap-4">
        <h3 className="font-display text-[1.35rem] font-semibold leading-tight text-[color:var(--color-text-base)]">
          {feature.title}
        </h3>
        <span className="tnum flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-[color:var(--color-border-bright)] bg-[rgba(232,185,74,0.08)] text-xs text-[color:var(--color-accent-bright)]">
          {String(index + 1).padStart(2, '0')}
        </span>
      </div>
      <p className="whitespace-pre-line text-sm leading-relaxed text-[color:var(--color-text-muted)]">
        {feature.description}
      </p>
      {feature.details.length > 0 ? (
        <ul className="mt-auto flex flex-col gap-2.5 border-t border-[color:var(--color-border-soft)] pt-4 text-sm leading-6 text-[color:var(--color-text-muted)]">
          {feature.details.map((detail) => (
            <li key={detail} className="flex gap-3">
              <span
                aria-hidden="true"
                className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-[color:var(--color-accent)]"
              />
              <span>{detail}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </article>
  );
}

function InstallationGuide({ copy }: { copy: TutorialPageCopy['installation'] }) {
  return (
    <section id="installation" className="scroll-mt-28 grid gap-5">
      <header className="grid gap-3">
        <h2 className="font-display text-3xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
          {copy.heading}
        </h2>
      </header>

      <ol className="grid gap-3">
        {copy.steps.map((step, index) => (
          <li
            key={step.title}
            className="surface-flat relative grid gap-4 overflow-hidden px-5 py-5 sm:grid-cols-[3.75rem_minmax(0,1fr)] sm:gap-5"
          >
            <span
              aria-hidden="true"
              className="absolute inset-y-0 left-0 w-px bg-[linear-gradient(180deg,rgba(232,185,74,0.72),rgba(232,185,74,0.08))]"
            />
            <span className="tnum flex h-12 w-12 items-center justify-center rounded-lg border border-[color:var(--color-border-bright)] bg-[rgba(232,185,74,0.08)] text-sm font-semibold text-[color:var(--color-accent-bright)]">
              {String(index + 1).padStart(2, '0')}
            </span>
            <span className="grid gap-2">
              <span className="font-display text-xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
                {step.title}
              </span>
              <span className="text-sm leading-7 text-[color:var(--color-text-muted)]">
                {step.description}
              </span>
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}

type HotkeyItem = TutorialPageCopy['quickStart']['groups'][number]['items'][number];

type HotkeyBinding = HotkeyItem['bindings'][number];

function Keycap({ binding }: { binding: HotkeyBinding }) {
  return (
    <span className="tnum inline-flex min-h-11 max-w-full items-center justify-center whitespace-nowrap rounded-lg border border-[color:var(--color-border-strong)] bg-[linear-gradient(180deg,rgba(232,185,74,0.18),rgba(232,185,74,0.06))] px-3 py-2 text-center text-xs font-semibold leading-tight text-[color:var(--color-accent-bright)] shadow-[inset_0_-2px_0_rgba(0,0,0,0.26),0_12px_28px_-18px_rgba(232,185,74,0.6)]">
      {binding.key}
    </span>
  );
}

function HotkeyCard({ item, className = '' }: { item: HotkeyItem; className?: string }) {
  const hasBindingActions = item.bindings.some((binding) => binding.action);
  const primaryBinding = item.bindings[0];
  const secondaryBindings = hasBindingActions ? item.bindings.slice(1) : [];

  return (
    <article
      className={`surface-flat flex flex-col gap-4 overflow-hidden p-5 transition duration-200 hover:border-[color:var(--color-border-bright)] ${className}`}
    >
      <div className="flex max-w-full flex-wrap items-center gap-2">
        {hasBindingActions && primaryBinding ? (
          <>
            <Keycap binding={primaryBinding} />
            {primaryBinding.action ? (
              <span className="min-w-0 text-xs font-medium leading-tight text-[color:var(--color-text-muted)]">
                {primaryBinding.action}
              </span>
            ) : null}
          </>
        ) : (
          item.bindings.map((binding) => <Keycap key={binding.key} binding={binding} />)
        )}
      </div>
      <div className="grid min-w-0 gap-1.5">
        <h3 className="font-display text-lg font-semibold leading-tight text-[color:var(--color-text-base)]">
          {item.title}
        </h3>
        <p className="whitespace-pre-line break-words text-sm leading-relaxed text-[color:var(--color-text-muted)]">
          {item.description}
        </p>
        {secondaryBindings.length > 0 ? (
          <div className="mt-1 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-[color:var(--color-text-faint)]">
            {secondaryBindings.map((binding, index) => (
              <span key={binding.key} className="inline-flex min-w-0 items-center gap-1.5">
                {index > 0 ? (
                  <span aria-hidden="true" className="text-[color:var(--color-border-strong)]">
                    ·
                  </span>
                ) : null}
                <span className="tnum font-medium text-[color:var(--color-accent-bright)]">
                  {binding.key}
                </span>
                <span className="whitespace-nowrap">{binding.action}</span>
              </span>
            ))}
          </div>
        ) : null}
      </div>
    </article>
  );
}

function QuickStart({ copy }: { copy: TutorialPageCopy['quickStart'] }) {
  return (
    <section id="hotkeys" className="scroll-mt-28 grid gap-5">
      <header className="grid gap-3">
        <h2 className="font-display text-3xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
          {copy.heading}
        </h2>
      </header>
      <div className="grid gap-8">
        {copy.groups.map((group) => (
          <section key={group.title} className="grid min-w-0 content-start gap-5">
            <header className="grid gap-2 border-l-2 border-[color:var(--color-accent)] pl-4">
              <h3 className="font-display text-xl font-semibold leading-tight text-[color:var(--color-text-base)]">
                {group.title}
              </h3>
              <p className="text-sm leading-6 text-[color:var(--color-text-muted)]">
                {group.description}
              </p>
              {group.note ? (
                <div className="mt-1">
                  <p className="inline-block min-w-0 break-words rounded-xl border border-[color:var(--color-border-soft)] bg-[rgba(232,185,74,0.04)] px-4 py-2.5 text-xs leading-5 text-[color:var(--color-text-faint)]">
                    {group.note}
                  </p>
                </div>
              ) : null}
            </header>
            <div className="grid items-stretch gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {group.items.map((item) => (
                <HotkeyCard key={item.title} item={item} />
              ))}
            </div>
          </section>
        ))}
      </div>
    </section>
  );
}

export default function TutorialPage({ location }: TutorialPageProps) {
  const { locale } = location;
  const copy = getSiteCopy(locale).tutorial;
  const downloadHref = location.navigation.items.find((item) => item.page === 'download')!.href;
  const supportHref = location.navigation.items.find((item) => item.page === 'support')!.href;

  return (
    <InfoPageShell locale={locale} location={location} title={copy.title} intro={copy.intro}>
      <section className="surface overflow-hidden p-5 sm:p-7">
        <div className="grid gap-5 sm:grid-cols-[6rem_minmax(0,1fr)] sm:items-center">
          <img
            src="/bazaarplusplus-icon.webp"
            alt=""
            aria-hidden="true"
            width={96}
            height={96}
            className="h-24 w-24 object-contain drop-shadow-[0_0_28px_rgba(232,185,74,0.38)]"
            decoding="async"
          />
          <div className="flex flex-wrap gap-3">
            <a
              href={downloadHref}
              className="inline-flex min-h-12 items-center justify-center gap-2 rounded-full bg-[color:var(--color-accent)] px-5 py-3 text-sm font-medium tracking-[0.04em] text-[#1a1306] shadow-[0_18px_36px_-12px_rgba(232,185,74,0.5)] transition hover:bg-[color:var(--color-accent-bright)]"
            >
              <span>{copy.primaryActionLabel}</span>
              <span aria-hidden="true">→</span>
            </a>
            <a
              href={supportHref}
              className="inline-flex min-h-12 items-center justify-center gap-2 rounded-full border border-[color:var(--color-accent)] px-5 py-3 text-sm font-medium tracking-[0.04em] text-[color:var(--color-accent-bright)] transition hover:bg-[color:var(--color-accent)] hover:text-[#1a1306]"
            >
              <span>{copy.secondaryActionLabel}</span>
              <span aria-hidden="true">→</span>
            </a>
          </div>
        </div>
      </section>

      <section id="features" className="scroll-mt-28 grid gap-6">
        <header className="grid gap-3">
          <h2 className="font-display text-3xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
            {copy.featureHeading}
          </h2>
        </header>
        <div className="grid gap-4 md:grid-cols-2">
          {copy.features.map((feature, index) => (
            <FeatureCard key={feature.title} feature={feature} index={index} />
          ))}
        </div>
      </section>

      <QuickStart copy={copy.quickStart} />

      <InstallationGuide copy={copy.installation} />
    </InfoPageShell>
  );
}
