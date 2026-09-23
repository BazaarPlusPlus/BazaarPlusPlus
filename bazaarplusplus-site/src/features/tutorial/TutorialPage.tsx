import Button from '../../shared/components/Button';
import { DownloadIcon } from '../../shared/components/icons';
import InfoPageShell from '../../shared/components/InfoPageShell';
import { SectionHeading } from '../../shared/components/PageLayout';
import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type TutorialPageCopy } from '../../content/site-copy';

type TutorialPageProps = {
  location: ResolvedSpaLocation;
};

function formatStepNumber(index: number): string {
  return String(index + 1).padStart(2, '0');
}

function FeatureCard({
  feature,
  index,
}: {
  feature: TutorialPageCopy['features'][number];
  index: number;
}) {
  return (
    <article className="panel flex flex-col gap-3 p-5 sm:p-6">
      <div className="flex items-start justify-between gap-4">
        <h3 className="text-base leading-snug font-semibold text-text-1">{feature.title}</h3>
        <span className="shrink-0 text-xs font-medium text-text-3 tabular-nums">
          {formatStepNumber(index)}
        </span>
      </div>
      <p className="text-sm whitespace-pre-line text-text-2">{feature.description}</p>
      {feature.details.length > 0 ? (
        <ul className="mt-auto flex flex-col gap-2 border-t border-line pt-3 text-sm text-text-2">
          {feature.details.map((detail) => (
            <li key={detail} className="flex gap-3">
              <span aria-hidden="true" className="mt-2.5 size-1 shrink-0 rounded-full bg-text-3" />
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
    <section id="installation" className="grid scroll-mt-24 gap-5">
      <SectionHeading>{copy.heading}</SectionHeading>

      <ol className="panel flex flex-col">
        {copy.steps.map((step, index) => (
          <li
            key={step.title}
            className="grid grid-cols-[2rem_minmax(0,1fr)] gap-3 border-b border-line px-5 py-4 last:border-b-0"
          >
            <span
              aria-hidden="true"
              className="flex size-6 items-center justify-center rounded-full border border-line-strong text-xs text-text-2 tabular-nums"
            >
              {index + 1}
            </span>
            <span className="grid gap-1">
              <span className="font-semibold text-text-1">{step.title}</span>
              <span className="text-sm text-text-2">{step.description}</span>
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
    <kbd className="inline-flex h-7 max-w-full items-center justify-center rounded-control border border-line-strong bg-canvas px-2 font-sans text-xs font-semibold whitespace-nowrap text-text-1 tabular-nums">
      {binding.key}
    </kbd>
  );
}

function HotkeyCard({ item }: { item: HotkeyItem }) {
  const hasBindingActions = item.bindings.some((binding) => binding.action);
  const primaryBinding = item.bindings[0];
  const secondaryBindings = hasBindingActions ? item.bindings.slice(1) : [];

  return (
    <article className="panel flex flex-col gap-3 p-5">
      <div className="flex max-w-full flex-wrap items-center gap-2">
        {hasBindingActions && primaryBinding ? (
          <>
            <Keycap binding={primaryBinding} />
            {primaryBinding.action ? (
              <span className="min-w-0 text-xs font-medium text-text-2">
                {primaryBinding.action}
              </span>
            ) : null}
          </>
        ) : (
          item.bindings.map((binding) => <Keycap key={binding.key} binding={binding} />)
        )}
      </div>
      <div className="grid min-w-0 gap-1">
        <h3 className="text-[15px] leading-snug font-semibold text-text-1">{item.title}</h3>
        <p className="text-sm break-words whitespace-pre-line text-text-2">{item.description}</p>
        {secondaryBindings.length > 0 ? (
          <div className="mt-1 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-text-3">
            {secondaryBindings.map((binding, index) => (
              <span key={binding.key} className="inline-flex min-w-0 items-center gap-1.5">
                {index > 0 ? <span aria-hidden="true">·</span> : null}
                <span className="font-semibold text-text-1 tabular-nums">{binding.key}</span>
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
    <section id="hotkeys" className="grid scroll-mt-24 gap-5">
      <SectionHeading>{copy.heading}</SectionHeading>
      <div className="grid gap-8">
        {copy.groups.map((group) => (
          <section key={group.title} className="grid min-w-0 content-start gap-4">
            <header className="grid gap-1">
              <h3 className="text-[15px] font-semibold text-text-1">{group.title}</h3>
              <p className="text-sm text-text-2">{group.description}</p>
              {group.note ? (
                <p className="mt-2 w-fit max-w-full rounded-panel border border-line bg-sidebar px-3 py-2 text-xs break-words text-text-2">
                  {group.note}
                </p>
              ) : null}
            </header>
            <div className="grid items-stretch gap-3 sm:grid-cols-2 lg:grid-cols-3">
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
    <InfoPageShell location={location} title={copy.title} intro={copy.intro}>
      <section className="panel flex flex-col gap-5 p-5 sm:flex-row sm:items-center sm:p-6">
        <img
          src="/bazaarplusplus-icon.webp"
          alt=""
          aria-hidden="true"
          width={64}
          height={64}
          className="size-16 object-contain"
          decoding="async"
        />
        <div className="flex flex-wrap gap-2">
          <Button variant="primary" href={downloadHref} icon={<DownloadIcon />}>
            {copy.primaryActionLabel}
          </Button>
          <Button href={supportHref}>{copy.secondaryActionLabel}</Button>
        </div>
      </section>

      <section id="features" className="grid scroll-mt-24 gap-5">
        <SectionHeading>{copy.featureHeading}</SectionHeading>
        <div className="grid gap-3 md:grid-cols-2">
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
