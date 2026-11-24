import { useEffect, useId, type InputHTMLAttributes } from 'react';

import aliasTable from '../../../../bazaarplusplus-analyzer/contracts/v6/hero-aliases.json';
import type { AccountRoutePage, Locale } from '../../app/router';
import {
  fillCopy,
  getSiteCopy,
  type AccountErrorKey,
  type RunOutcomeTier
} from '../../content/site-copy';
import { getIntlLocale } from '../../shared/lib/intl-locale';
import { errorCode, isSignedOut, type RunSummary } from './account-api';

const aliases = new Map(Object.entries(aliasTable.aliases));

export function heroName(hero: string): string {
  return aliases.get(hero) ?? hero;
}

/** Matches the mod's HistoryPanelFormatter.GetRunOutcomeTier, as the installer does. */
export function runOutcomeTier(
  victories: number | null,
  losses: number | null
): RunOutcomeTier {
  const wins = victories ?? 0;
  const battles = wins + (losses ?? 0);
  if (wins === 10 && battles === 10) return 'diamond';
  if (wins >= 10) return 'gold';
  if (wins >= 7) return 'silver';
  if (wins >= 4) return 'bronze';
  return 'misfortune';
}

export function RunOutcome({
  locale,
  victories,
  losses
}: {
  locale: Locale;
  victories: number | null;
  losses: number | null;
}) {
  const tier = runOutcomeTier(victories, losses);
  return (
    <span className="bpp-outcome" data-tier={tier}>
      {getSiteCopy(locale).account.outcomes[tier]}
    </span>
  );
}

const KNOWN_ERRORS = new Set<string>([
  'invalid_credentials',
  'invalid_verification_code',
  'invalid_password',
  'email_already_registered',
  'game_account_already_bound',
  'rate_limited',
  'captcha_failed',
  'captcha_unavailable'
]);

export function errorMessage(locale: Locale, error: unknown): string {
  const code = errorCode(error);
  const key = (
    code && KNOWN_ERRORS.has(code) ? code : 'request_failed'
  ) as AccountErrorKey;
  return getSiteCopy(locale).account.errors[key];
}

export function FormError({ children }: { children: string | null }) {
  return children ? (
    <p role="alert" className="text-small text-danger-text">
      {children}
    </p>
  ) : null;
}

export function Field({
  label,
  ...input
}: { label: string } & InputHTMLAttributes<HTMLInputElement>) {
  const id = useId();
  return (
    <div className="bpp-field">
      <label htmlFor={id} className="bpp-field-label">
        {label}
      </label>
      <input id={id} className="bpp-input w-full" {...input} />
    </div>
  );
}

/** Hands over to sign-in once any request reports the session is gone. */
export function useSignedOutRedirect(
  errors: unknown[],
  replacePage: (page: AccountRoutePage) => void
): boolean {
  const signedOut = errors.some(isSignedOut);
  useEffect(() => {
    if (signedOut) replacePage('login');
  }, [signedOut, replacePage]);
  return signedOut;
}

export const ACCOUNT_QUERY = ['account'] as const;

/** Wins and losses, final rank, rating with its change, and mode. */
export function RunFacts({ run, locale }: { run: RunSummary; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const delta = new Intl.NumberFormat(getIntlLocale(locale), {
    signDisplay: 'exceptZero'
  });
  const rating =
    run.final_player_rating == null
      ? null
      : `${run.final_player_rating}${
          run.final_player_rating_delta == null
            ? ''
            : ` (${delta.format(run.final_player_rating_delta)})`
        }`;
  return (
    <>
      <span className="text-body-medium text-text-1">
        {fillCopy(copy.winLoss, {
          wins: run.victories ?? '—',
          losses: run.losses ?? '—'
        })}
      </span>
      {[run.final_player_rank, rating, run.game_mode]
        .filter((value) => value != null)
        .map((value) => (
          <span key={value} className="text-text-2">
            {value}
          </span>
        ))}
    </>
  );
}
