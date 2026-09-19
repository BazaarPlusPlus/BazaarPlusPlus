import { useId, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';

import DialogShell from '../../shared/components/DialogShell';
import InfoPageShell from '../../shared/components/InfoPageShell';
import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, KOFI_URL, type SupportPageCopy } from '../../content/site-copy';
import { loadSupporters, orderSupportersForDisplay, type Supporter } from './supporters-data';
import { wechatPayQrSvg } from './wechat-pay';

type SupportPageProps = {
  location: ResolvedSpaLocation;
};

const SUPPORTER_SKELETON_WIDTHS = ['w-20', 'w-24', 'w-28', 'w-16', 'w-32', 'w-24', 'w-20', 'w-28'];

function tierClassName(tier: number): string {
  if (tier >= 4) {
    return 'border-[rgba(232,185,74,0.45)] bg-[rgba(232,185,74,0.06)] text-[color:var(--color-accent-bright)] shadow-[0_18px_36px_-22px_rgba(232,185,74,0.55)]';
  }
  if (tier === 3) {
    return 'border-[rgba(232,185,74,0.28)] bg-[rgba(232,185,74,0.03)] text-[color:var(--color-text-base)]';
  }
  if (tier === 2) {
    return 'border-[color:var(--color-border-soft)] bg-transparent text-[color:var(--color-text-muted)]';
  }
  return 'border-dashed border-[color:var(--color-border-soft)] bg-transparent text-[color:var(--color-text-muted)]';
}

function tierFontClassName(tier: number): string {
  if (tier >= 4) {
    return 'font-display text-base font-semibold tracking-tight';
  }
  if (tier === 3) {
    return 'font-display text-[0.95rem] font-medium tracking-tight';
  }
  return 'text-sm';
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
      <ul className="flex flex-wrap gap-3" aria-busy="true" aria-label={copy.heading}>
        {SUPPORTER_SKELETON_WIDTHS.map((width, index) => (
          <li
            key={index}
            className={`inline-flex h-9 ${width} items-center overflow-hidden rounded-full border border-[color:var(--color-border-soft)] bg-[rgba(232,185,74,0.03)]`}
          >
            <span className="shimmer block h-full w-full" />
          </li>
        ))}
      </ul>
    );
  }

  if (isError) {
    return (
      <p className="text-sm leading-6 text-[color:var(--color-text-muted)]">{copy.errorNote}</p>
    );
  }

  if (ordered.length === 0) {
    return (
      <p className="text-sm leading-6 text-[color:var(--color-text-muted)]">{copy.emptyNote}</p>
    );
  }

  return (
    <ul className="flex flex-wrap gap-3">
      {ordered.map((supporter, index) => (
        <li
          key={`${supporter.name}:${index}`}
          className={`inline-flex items-center rounded-full border px-4 py-2 transition ${tierClassName(supporter.tier)}`}
        >
          <span className={tierFontClassName(supporter.tier)}>{supporter.name}</span>
        </li>
      ))}
    </ul>
  );
}

export default function SupportPage({ location }: SupportPageProps) {
  const { locale } = location;
  const copy = getSiteCopy(locale).support;
  const [wechatOpen, setWechatOpen] = useState(false);
  const wechatTitleId = useId();

  const { data, isLoading, isError } = useQuery({
    queryKey: ['supporters'],
    queryFn: ({ signal }) => loadSupporters(signal),
  });

  const orderedSupporters = useMemo(() => (data ? orderSupportersForDisplay(data) : []), [data]);

  return (
    <InfoPageShell locale={locale} location={location} title={copy.title}>
      <section className="grid gap-6">
        <p className="max-w-2xl text-sm leading-7 text-[color:var(--color-text-muted)]">
          {copy.intro}
        </p>

        <div className="grid gap-6 md:grid-cols-2">
          <article className="surface flex flex-col gap-5 p-7">
            <div className="flex items-baseline justify-between gap-3">
              <h2 className="font-display text-[1.45rem] font-semibold leading-tight text-[color:var(--color-text-base)]">
                {copy.wechat.title}
              </h2>
              <span className="text-[0.7rem] uppercase tracking-[0.18em] text-[color:var(--color-text-faint)]">
                {copy.wechat.regionLabel}
              </span>
            </div>
            <p className="text-sm leading-6 text-[color:var(--color-text-muted)]">
              {copy.wechat.description}
            </p>
            <button
              type="button"
              onClick={() => setWechatOpen(true)}
              className="mt-auto inline-flex items-center justify-center gap-2 rounded-full bg-[color:var(--color-accent)] px-5 py-3 text-sm font-medium tracking-[0.04em] text-[#1a1306] shadow-[0_18px_36px_-12px_rgba(232,185,74,0.5)] transition hover:bg-[color:var(--color-accent-bright)]"
            >
              <span>{copy.wechat.actionLabel}</span>
              <span aria-hidden="true">→</span>
            </button>
          </article>

          <article className="surface flex flex-col gap-5 p-7">
            <div className="flex items-baseline justify-between gap-3">
              <h2 className="font-display text-[1.45rem] font-semibold leading-tight text-[color:var(--color-text-base)]">
                {copy.kofi.title}
              </h2>
              <span className="text-[0.7rem] uppercase tracking-[0.18em] text-[color:var(--color-text-faint)]">
                {copy.kofi.regionLabel}
              </span>
            </div>
            <p className="text-sm leading-6 text-[color:var(--color-text-muted)]">
              {copy.kofi.description}
            </p>
            <a
              href={KOFI_URL}
              target="_blank"
              rel="noreferrer"
              className="group mt-auto inline-flex items-center justify-center gap-2 rounded-full border border-[color:var(--color-accent)] px-5 py-3 text-sm font-medium tracking-[0.04em] text-[color:var(--color-accent-bright)] transition hover:bg-[color:var(--color-accent)] hover:text-[#1a1306]"
            >
              <span>{copy.kofi.actionLabel}</span>
              <span aria-hidden="true" className="transition group-hover:translate-x-0.5">
                ↗
              </span>
            </a>
          </article>
        </div>
      </section>

      <section
        aria-label={copy.supporters.heading}
        className="flex flex-col gap-6 border-t border-[color:var(--color-border-soft)] pt-10"
      >
        <header className="grid gap-3">
          <p className="eyebrow eyebrow-rule">{copy.supporters.heading}</p>
          <p className="max-w-2xl text-sm leading-7 text-[color:var(--color-text-muted)]">
            {copy.supporters.intro}
          </p>
        </header>

        <SupportersBody
          copy={copy.supporters}
          isLoading={isLoading}
          isError={isError}
          ordered={orderedSupporters}
        />

        <p className="text-xs leading-6 text-[color:var(--color-text-faint)]">
          {copy.supporters.unnamedNote}
        </p>
      </section>

      <DialogShell
        open={wechatOpen}
        onClose={() => setWechatOpen(false)}
        labelledBy={wechatTitleId}
        closeLabel={copy.closeLabel}
      >
        <p className="eyebrow eyebrow-rule">{copy.wechat.modalSubtitle}</p>
        <h2
          id={wechatTitleId}
          className="mt-2 font-display text-2xl font-semibold tracking-tight text-[color:var(--color-text-base)]"
        >
          {copy.wechat.modalTitle}
        </h2>
        <div className="mt-6 flex justify-center">
          <div
            className="flex h-56 w-56 items-center justify-center rounded-2xl border border-[color:var(--color-border-soft)] bg-white p-3"
            dangerouslySetInnerHTML={{ __html: wechatPayQrSvg }}
            aria-label={copy.wechat.qrAriaLabel}
            role="img"
          />
        </div>
        <p className="mt-5 text-sm leading-6 text-[color:var(--color-text-muted)]">
          {copy.wechat.modalHint}
        </p>
      </DialogShell>
    </InfoPageShell>
  );
}
