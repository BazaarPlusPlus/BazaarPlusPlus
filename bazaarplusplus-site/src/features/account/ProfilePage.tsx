import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Dialog,
  DialogBody,
  DialogCard,
  DialogFooter,
  DialogHeader,
  StatusPanel
} from '@bpp/ui';

import {
  buildRouteHref,
  type AccountRoutePage,
  type Locale,
  type ResolvedSpaLocation
} from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import PageLayout, { PageHeading } from '../../shared/components/PageLayout';
import { getIntlLocale } from '../../shared/lib/intl-locale';
import { loadMe, postAccount, type BindCode, type Me } from './account-api';
import {
  ACCOUNT_QUERY,
  Field,
  FormError,
  errorMessage,
  useSignedOutRedirect
} from './account-ui';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

function useNow(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active]);
  return now;
}

function countdown(ms: number): string {
  const seconds = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

type Redirect = Props['replacePage'];

function BindCodePanel({
  locale,
  replacePage
}: {
  locale: Locale;
  replacePage: Redirect;
}) {
  const copy = getSiteCopy(locale).account;
  const [code, setCode] = useState<BindCode | null>(null);
  const [copied, setCopied] = useState(false);
  const generate = useMutation({
    mutationFn: () => postAccount<BindCode>('bind-code'),
    onSuccess: (next) => {
      setCode(next);
      setCopied(false);
    }
  });
  useSignedOutRedirect([generate.error], replacePage);
  const now = useNow(code !== null);
  const remaining = code ? code.expires_at_ms - now : 0;
  const live = code !== null && remaining > 0;
  const shown = code ? `${code.code.slice(0, 4)}-${code.code.slice(4)}` : '';

  if (!live) {
    return (
      <div className="bpp-row">
        <div className="bpp-row-copy">
          <span className="bpp-row-title">{copy.game}</span>
          {generate.isError ? (
            <span role="alert" className="text-small text-danger-text">
              {errorMessage(locale, generate.error)}
            </span>
          ) : (
            <span className="text-small text-text-3">
              {code ? copy.expired : copy.unlinked}
            </span>
          )}
        </div>
        <Button
          variant="primary"
          size="sm"
          busy={generate.isPending}
          onClick={() => generate.mutate()}
        >
          {code ? copy.regenerateCode : copy.generateCode}
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center gap-4 px-4 py-6 text-center">
      <span className="text-small text-text-3">{copy.game}</span>
      <div className="flex flex-wrap items-center justify-center gap-3">
        <output
          aria-label={copy.generateCode}
          className="rounded-control border border-line bg-canvas px-4 py-2 font-mono text-display font-semibold tracking-[0.12em] text-accent tabular-nums select-all"
        >
          {shown}
        </output>
        <Button
          size="sm"
          onClick={() => {
            void navigator.clipboard
              .writeText(code.code)
              .then(() => setCopied(true));
          }}
        >
          {copied ? copy.copied : copy.copy}
        </Button>
      </div>
      <p className="max-w-[40ch] text-body text-text-2">{copy.bindSteps}</p>
      <div className="flex items-center gap-3 text-small text-text-3">
        <span className="tabular-nums" role="timer">
          {fillCopy(copy.expiresIn, { time: countdown(remaining) })}
        </span>
        <Button
          size="sm"
          variant="ghost"
          busy={generate.isPending}
          onClick={() => generate.mutate()}
        >
          {copy.regenerateCode}
        </Button>
      </div>
    </div>
  );
}

function ConfirmDialog({
  id,
  title,
  body,
  busy,
  error,
  confirmDisabled,
  onConfirm,
  onClose,
  children,
  cancel
}: {
  id: string;
  title: string;
  body: string;
  busy: boolean;
  error: string | null;
  confirmDisabled?: boolean;
  onConfirm: () => void;
  onClose: () => void;
  children?: ReactNode;
  cancel: string;
}) {
  return (
    <Dialog labelledBy={id} dismissible={!busy} onClose={onClose}>
      <DialogCard size="sm">
        <DialogHeader titleId={id} title={title} />
        <DialogBody>
          <form
            id={`${id}-form`}
            className="flex flex-col gap-4"
            onSubmit={(event) => {
              event.preventDefault();
              onConfirm();
            }}
          >
            <p>{body}</p>
            {children}
            <FormError>{error}</FormError>
          </form>
        </DialogBody>
        <DialogFooter>
          <Button disabled={busy} onClick={onClose}>
            {cancel}
          </Button>
          <Button
            type="submit"
            form={`${id}-form`}
            variant="danger"
            busy={busy}
            disabled={confirmDisabled}
          >
            {title}
          </Button>
        </DialogFooter>
      </DialogCard>
    </Dialog>
  );
}

function GameAccount({
  me,
  locale,
  replacePage
}: {
  me: Me;
  locale: Locale;
  replacePage: Redirect;
}) {
  const copy = getSiteCopy(locale).account;
  const queryClient = useQueryClient();
  const [confirm, setConfirm] = useState(false);
  const unlink = useMutation({
    mutationFn: () => postAccount('unlink'),
    onSuccess: () => {
      setConfirm(false);
      // Unlinking hides the Records: drop their pages instead of refetching them.
      for (const key of ['runs', 'run', 'run-detail']) {
        queryClient.removeQueries({ queryKey: [...ACCOUNT_QUERY, key] });
      }
      void queryClient.invalidateQueries({ queryKey: ACCOUNT_QUERY });
    }
  });
  useSignedOutRedirect([unlink.error], replacePage);
  if (!me.game) {
    return (
      <section className="bpp-panel">
        <BindCodePanel locale={locale} replacePage={replacePage} />
      </section>
    );
  }
  const date = new Intl.DateTimeFormat(getIntlLocale(locale), {
    dateStyle: 'medium'
  });
  return (
    <section className="bpp-panel">
      <div className="bpp-row">
        <div className="bpp-row-copy">
          <span className="bpp-row-title truncate">{me.game.user_name}</span>
          <span className="text-small text-text-3">
            {fillCopy(copy.linkedAt, {
              date: date.format(me.game.linked_at_ms)
            })}
          </span>
        </div>
        <Button
          size="sm"
          onClick={() => {
            unlink.reset();
            setConfirm(true);
          }}
        >
          {copy.unlink}
        </Button>
      </div>
      {confirm && (
        <ConfirmDialog
          id="unlink-title"
          title={copy.unlink}
          body={copy.unlinkWarning}
          cancel={copy.cancel}
          busy={unlink.isPending}
          error={unlink.isError ? errorMessage(locale, unlink.error) : null}
          onConfirm={() => unlink.mutate()}
          onClose={() => setConfirm(false)}
        />
      )}
    </section>
  );
}

function DeleteAccount({
  locale,
  onDeleted
}: {
  locale: Locale;
  onDeleted: () => void;
}) {
  const copy = getSiteCopy(locale).account;
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState('');
  const remove = useMutation({
    mutationFn: () => postAccount('account/delete', { password }),
    onSuccess: onDeleted
  });
  return (
    <div>
      <Button
        variant="danger"
        size="sm"
        onClick={() => {
          remove.reset();
          setPassword('');
          setOpen(true);
        }}
      >
        {copy.deleteAccount}
      </Button>
      {open && (
        <ConfirmDialog
          id="delete-title"
          title={copy.deleteAccount}
          body={copy.deleteWarning}
          cancel={copy.cancel}
          busy={remove.isPending}
          confirmDisabled={password.length === 0}
          error={remove.isError ? errorMessage(locale, remove.error) : null}
          onConfirm={() => remove.mutate()}
          onClose={() => setOpen(false)}
        >
          <Field
            label={copy.password}
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </ConfirmDialog>
      )}
    </div>
  );
}

export default function ProfilePage({ location, replacePage }: Props) {
  const siteCopy = getSiteCopy(location.locale);
  const copy = siteCopy.account;
  const queryClient = useQueryClient();
  const me = useQuery({
    queryKey: [...ACCOUNT_QUERY, 'me'],
    queryFn: ({ signal }) => loadMe(signal),
    staleTime: 0,
    retry: false,
    // A bind finishes in the game; coming back to the tab shows it.
    refetchOnWindowFocus: true
  });
  function leave() {
    queryClient.removeQueries({ queryKey: ACCOUNT_QUERY });
    replacePage('login');
  }
  const logout = useMutation({
    mutationFn: () => postAccount('logout'),
    onSuccess: leave
  });
  const signedOut = useSignedOutRedirect([me.error, logout.error], replacePage);
  if (signedOut) return null;
  return (
    <PageLayout location={location} busy={me.isPending}>
      <PageHeading
        title={siteCopy.pageTitles.account}
        intro={me.data?.email}
        actions={
          me.data && (
            <Button busy={logout.isPending} onClick={() => logout.mutate()}>
              {copy.logout}
            </Button>
          )
        }
      />
      {me.isPending && (
        <StatusPanel kind="loading" compact title={copy.loading} />
      )}
      {me.isError && (
        <StatusPanel
          kind="error"
          title={copy.loadError}
          actions={
            <Button onClick={() => void me.refetch()}>{copy.retry}</Button>
          }
        />
      )}
      {logout.isError && (
        <FormError>{errorMessage(location.locale, logout.error)}</FormError>
      )}
      {me.data && (
        <>
          <GameAccount
            me={me.data}
            locale={location.locale}
            replacePage={replacePage}
          />
          {me.data.game && (
            <Button
              className="self-start"
              href={buildRouteHref('/records', location.locale)}
            >
              {copy.allRecords} →
            </Button>
          )}
          <DeleteAccount locale={location.locale} onDeleted={leave} />
        </>
      )}
    </PageLayout>
  );
}
