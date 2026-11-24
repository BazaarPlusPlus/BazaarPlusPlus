import { useEffect, useState, type FormEvent } from 'react';
import { useInfiniteQuery, useMutation } from '@tanstack/react-query';
import {
  Button,
  Dialog,
  DialogCard,
  DialogHeader,
  DialogBody,
  DialogFooter
} from '@bpp/ui';

import aliasTable from '../../../../bazaarplusplus-analyzer/contracts/v6/hero-aliases.json';
import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import PageLayout, { PageHeading } from '../../shared/components/PageLayout';
import { getIntlLocale } from '../../shared/lib/intl-locale';
import { AccountHttpError, loadRecords, postAccount } from './account-api';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: 'login' | 'records') => void;
};
const aliases = new Map(Object.entries(aliasTable.aliases));

function Login({ location, replacePage }: Props) {
  const copy = getSiteCopy(location.locale);
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [sent, setSent] = useState(false);
  const login = useMutation({
    mutationFn: () =>
      sent
        ? postAccount('login/confirm', { email, code })
        : postAccount('login/start', { email })
  });
  function submit(event: FormEvent) {
    event.preventDefault();
    login.mutate(undefined, {
      onSuccess: () => (sent ? replacePage('records') : setSent(true))
    });
  }
  const inputClass =
    'rounded-control border border-line bg-canvas px-3 py-2 text-body';
  return (
    <PageLayout location={location}>
      <PageHeading title={copy.pageTitles.login} />
      <form className="flex max-w-md flex-col gap-4" onSubmit={submit}>
        <label className="grid gap-1">
          {copy.account.email}
          <input
            className={inputClass}
            type="email"
            autoComplete="email"
            value={email}
            disabled={sent || login.isPending}
            required
            onChange={(e) => setEmail(e.target.value)}
          />
        </label>
        {sent && (
          <>
            <p role="status">{copy.account.sent}</p>
            <label className="grid gap-1">
              {copy.account.code}
              <input
                className={inputClass}
                inputMode="numeric"
                autoComplete="one-time-code"
                pattern="[0-9]{6}"
                maxLength={6}
                value={code}
                required
                onChange={(e) => setCode(e.target.value)}
              />
            </label>
          </>
        )}
        {login.isError && (
          <p role="alert">
            {sent &&
            login.error instanceof AccountHttpError &&
            [400, 401, 403].includes(login.error.status)
              ? copy.account.invalid
              : copy.account.error}
          </p>
        )}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" variant="primary" busy={login.isPending}>
            {sent ? copy.pageTitles.login : copy.account.send}
          </Button>
          {sent && (
            <Button
              disabled={login.isPending}
              onClick={() => {
                setSent(false);
                setCode('');
                login.reset();
              }}
            >
              {copy.account.change}
            </Button>
          )}
        </div>
      </form>
    </PageLayout>
  );
}

function Records({ location, replacePage }: Props) {
  const copy = getSiteCopy(location.locale);
  const labels = copy.account;
  const [confirm, setConfirm] = useState(false);
  const records = useInfiniteQuery({
    queryKey: ['account-records'],
    queryFn: ({ pageParam, signal }) => loadRecords(pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.next_cursor,
    gcTime: 0,
    staleTime: 0,
    retry: false
  });
  const action = useMutation({
    mutationFn: (path: 'logout' | 'unbind') => postAccount(path)
  });
  const signedOut = [records.error, action.error].some(
    (error) => error instanceof AccountHttpError && error.status === 401
  );
  useEffect(() => {
    if (signedOut) replacePage('login');
  }, [signedOut, replacePage]);
  function finish(path: 'logout' | 'unbind') {
    action.mutate(path, {
      onSuccess: () => replacePage('login'),
      onError: () => setConfirm(false)
    });
  }
  const account = records.data?.pages[0];
  const runs = records.data?.pages.flatMap((page) => page.runs) ?? [];
  const date = new Intl.DateTimeFormat(getIntlLocale(location.locale), {
    dateStyle: 'medium',
    timeStyle: 'short'
  });
  const delta = new Intl.NumberFormat(getIntlLocale(location.locale), {
    signDisplay: 'exceptZero'
  });
  if (signedOut) return null;
  return (
    <PageLayout location={location} width="wide">
      <PageHeading
        title={copy.pageTitles.records}
        intro={account && `${account.user_name} · ${account.email_masked}`}
        actions={
          account && (
            <>
              <Button
                disabled={action.isPending}
                onClick={() => finish('logout')}
              >
                {labels.logout}
              </Button>
              <Button
                disabled={action.isPending}
                onClick={() => {
                  action.reset();
                  setConfirm(true);
                }}
              >
                {labels.unbind}
              </Button>
            </>
          )
        }
      />
      {records.isPending && <p role="status">{labels.loading}</p>}
      {action.isError && <p role="alert">{labels.error}</p>}
      {records.isError && (
        <div role="alert">
          {labels.error}{' '}
          <Button
            onClick={() =>
              void (records.isFetchNextPageError
                ? records.fetchNextPage()
                : records.refetch())
            }
          >
            {copy.common.error.retry}
          </Button>
        </div>
      )}
      {account &&
        (runs.length ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-body">
              <thead>
                <tr>
                  {[
                    labels.hero,
                    labels.result,
                    labels.rank,
                    labels.rating,
                    labels.ended,
                    labels.mode
                  ].map((label) => (
                    <th className="border-b border-line p-2" key={label}>
                      {label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {runs.map((run) => (
                  <tr className="border-b border-line" key={run.run_id}>
                    <td className="p-2">
                      {run.hero == null
                        ? '—'
                        : (aliases.get(run.hero) ?? run.hero)}
                    </td>
                    <td className="p-2">
                      {run.victories ?? '—'} / {run.losses ?? '—'}
                    </td>
                    <td className="p-2">{run.final_player_rank ?? '—'}</td>
                    <td className="p-2">
                      {run.final_player_rating ?? '—'}{' '}
                      {run.final_player_rating_delta != null &&
                        `(${delta.format(run.final_player_rating_delta)})`}
                    </td>
                    <td className="p-2">
                      {run.ended_at_ms == null
                        ? '—'
                        : date.format(run.ended_at_ms)}
                    </td>
                    <td className="p-2">{run.game_mode ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p>{labels.empty}</p>
        ))}
      {records.hasNextPage && !records.isFetchNextPageError && (
        <div>
          <Button
            busy={records.isFetchingNextPage}
            onClick={() => void records.fetchNextPage()}
          >
            {labels.more}
          </Button>
        </div>
      )}
      {confirm && (
        <Dialog
          labelledBy="unbind-title"
          dismissible={!action.isPending}
          onClose={() => setConfirm(false)}
        >
          <DialogCard size="sm">
            <DialogHeader titleId="unbind-title" title={labels.unbind} />
            <DialogBody>{labels.warning}</DialogBody>
            <DialogFooter>
              <Button
                disabled={action.isPending}
                onClick={() => setConfirm(false)}
              >
                {labels.cancel}
              </Button>
              <Button
                variant="danger"
                busy={action.isPending}
                onClick={() => finish('unbind')}
              >
                {labels.unbind}
              </Button>
            </DialogFooter>
          </DialogCard>
        </Dialog>
      )}
    </PageLayout>
  );
}

export default function AccountPage(props: Props) {
  return props.location.route.page === 'login' ? (
    <Login {...props} />
  ) : (
    <Records {...props} />
  );
}
