import { useId, useMemo, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';

import Badge from '../../shared/components/Badge';
import Button from '../../shared/components/Button';
import DialogShell from '../../shared/components/DialogShell';
import { ExternalLinkIcon } from '../../shared/components/icons';
import InfoPageShell from '../../shared/components/InfoPageShell';
import { SectionHeading } from '../../shared/components/PageLayout';
import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, KOFI_URL, type SupportPageCopy } from '../../content/site-copy';
import { loadSupporters, orderSupportersForDisplay, type Supporter } from './supporters-data';
import wechatPayQrSvg from '../../../../bazaarplusplus-installer/static/support/wechat-pay.svg?raw';

type SupportPageProps = {
  location: ResolvedSpaLocation;
};

const SUPPORTER_SKELETON_WIDTHS = ['w-20', 'w-24', 'w-28', 'w-16', 'w-32', 'w-24', 'w-20', 'w-28'];

function tierClassName(tier: number): string {
  if (tier >= 4) {
    return 'border-accent-line bg-accent-subtle font-semibold text-text-1';
  }
  if (tier === 3) {
    return 'border-line-strong bg-panel font-medium text-text-1';
  }
  if (tier === 2) {
    return 'border-line bg-panel text-text-2';
  }
  return 'border-dashed border-line-strong text-text-2';
}

type SupportersBodyProps = {
  copy: SupportPageCopy['supporters'];
  isLoading: boolean;
  isError: boolean;
  ordered: Supporter[];
};

function SupportersBody({ copy, isLoading, isError, ordered }: SupportersBodyProps) {
  if (isLoading) {
    return (
      <ul className="flex flex-wrap gap-2" aria-busy="true" aria-label={copy.heading}>
        {SUPPORTER_SKELETON_WIDTHS.map((width, index) => (
          <li
            key={index}
            className={`h-8 ${width} rounded-full bg-hover motion-safe:animate-pulse`}
          />
        ))}
      </ul>
    );
  }

  if (isError) {
    return <p className="text-sm text-text-2">{copy.errorNote}</p>;
  }

  if (ordered.length === 0) {
    return <p className="text-sm text-text-2">{copy.emptyNote}</p>;
  }

  return (
    <ul className="flex flex-wrap gap-2">
      {ordered.map((supporter, index) => (
        <li
          key={`${supporter.name}:${index}`}
          className={`inline-flex h-8 items-center rounded-full border px-3.5 text-sm ${tierClassName(supporter.tier)}`}
        >
          {supporter.name}
        </li>
      ))}
    </ul>
  );
}

function SupportOption({
  title,
  regionLabel,
  description,
  action,
}: {
  title: string;
  regionLabel: string;
  description: string;
  action: ReactNode;
}) {
  return (
    <article className="panel flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between gap-3">
        <SectionHeading>{title}</SectionHeading>
        <Badge>{regionLabel}</Badge>
      </div>
      <p className="text-sm text-text-2">{description}</p>
      <div className="mt-auto pt-2">{action}</div>
    </article>
  );
}

export default function SupportPage({ location }: SupportPageProps) {
  const { locale } = location;
  const copy = getSiteCopy(locale).support;
  const [wechatOpen, setWechatOpen] = useState(false);
  const wechatTitleId = useId();
  const supportersHeadingId = useId();

  const { data, isLoading, isError } = useQuery({
    queryKey: ['supporters'],
    queryFn: ({ signal }) => loadSupporters(signal),
  });

  const orderedSupporters = useMemo(() => (data ? orderSupportersForDisplay(data) : []), [data]);

  return (
    <InfoPageShell location={location} title={copy.title} intro={copy.intro}>
      <section className="grid gap-4 md:grid-cols-2">
        <SupportOption
          title={copy.wechat.title}
          regionLabel={copy.wechat.regionLabel}
          description={copy.wechat.description}
          action={
            <Button variant="primary" onClick={() => setWechatOpen(true)}>
              {copy.wechat.actionLabel}
            </Button>
          }
        />
        <SupportOption
          title={copy.kofi.title}
          regionLabel={copy.kofi.regionLabel}
          description={copy.kofi.description}
          action={
            <Button href={KOFI_URL} target="_blank" rel="noreferrer" icon={<ExternalLinkIcon />}>
              {copy.kofi.actionLabel}
            </Button>
          }
        />
      </section>

      <section aria-labelledby={supportersHeadingId} className="flex flex-col gap-5">
        <header className="flex flex-col gap-2">
          <SectionHeading id={supportersHeadingId}>{copy.supporters.heading}</SectionHeading>
          <p className="max-w-2xl text-sm text-text-2">{copy.supporters.intro}</p>
        </header>

        <SupportersBody
          copy={copy.supporters}
          isLoading={isLoading}
          isError={isError}
          ordered={orderedSupporters}
        />

        <p className="text-xs text-text-3">{copy.supporters.unnamedNote}</p>
      </section>

      <DialogShell
        open={wechatOpen}
        onClose={() => setWechatOpen(false)}
        labelledBy={wechatTitleId}
        closeLabel={copy.closeLabel}
      >
        <h2 id={wechatTitleId} className="pr-10 text-lg font-semibold text-text-1">
          {copy.wechat.modalTitle}
        </h2>
        <p className="mt-0.5 text-[13px] text-text-3">{copy.wechat.modalSubtitle}</p>
        <div className="mt-5 flex justify-center">
          <div
            className="flex size-56 items-center justify-center rounded-panel bg-white p-3"
            dangerouslySetInnerHTML={{ __html: wechatPayQrSvg }}
            aria-label={copy.wechat.qrAriaLabel}
            role="img"
          />
        </div>
        <p className="mt-5 text-sm text-text-2">{copy.wechat.modalHint}</p>
      </DialogShell>
    </InfoPageShell>
  );
}
