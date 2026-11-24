import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@bpp/ui';

import {
  buildRouteHref,
  type AccountRoutePage,
  type ResolvedSpaLocation
} from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import PageLayout from '../../shared/components/PageLayout';
import { postAccount } from './account-api';
import { ACCOUNT_QUERY, Field, FormError, errorMessage } from './account-ui';
import { useTurnstile } from './turnstile';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

function AuthCard({
  location,
  title,
  links,
  children
}: {
  location: ResolvedSpaLocation;
  title: string;
  links: Array<{ href: string; label: string }>;
  children: ReactNode;
}) {
  return (
    <PageLayout location={location}>
      <div className="mx-auto flex w-full max-w-sm flex-col gap-6">
        <h1 className="text-display leading-[1.3] font-semibold text-text-1">
          {title}
        </h1>
        <div className="bpp-panel bpp-panel-pad">{children}</div>
        <nav className="flex justify-center gap-6 text-small">
          {links.map((link) => (
            <a
              key={link.href}
              href={link.href}
              className="text-text-2 no-underline hover:text-text-1"
            >
              {link.label}
            </a>
          ))}
        </nav>
      </div>
    </PageLayout>
  );
}

function useSignIn(replacePage: Props['replacePage']) {
  const queryClient = useQueryClient();
  return () => {
    queryClient.removeQueries({ queryKey: ACCOUNT_QUERY });
    replacePage('account');
  };
}

function Login({ location, replacePage }: Props) {
  const copy = getSiteCopy(location.locale).account;
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const signIn = useSignIn(replacePage);
  const turnstile = useTurnstile(location.locale);
  const login = useMutation({
    mutationFn: (token: string) =>
      postAccount('login', { email, password, turnstile_token: token }),
    onSuccess: signIn,
    onSettled: turnstile.reset
  });
  const href = (path: string) => buildRouteHref(path, location.locale);
  return (
    <AuthCard
      location={location}
      title={copy.login}
      links={[
        { href: href('/register'), label: copy.register },
        { href: href('/reset'), label: copy.forgot }
      ]}
    >
      <form
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          if (turnstile.token) login.mutate(turnstile.token);
        }}
      >
        <Field
          label={copy.email}
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <Field
          label={copy.password}
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        {turnstile.widget}
        <FormError>
          {login.isError
            ? errorMessage(location.locale, login.error)
            : turnstile.unavailable
              ? copy.errors.captcha_unavailable
              : null}
        </FormError>
        <Button
          type="submit"
          variant="primary"
          busy={login.isPending}
          disabled={!turnstile.token}
        >
          {copy.login}
        </Button>
      </form>
    </AuthCard>
  );
}

/** Register and reset: an email step, then the emailed code with a new password. */
function CodeFlow({
  location,
  replacePage,
  purpose
}: Props & { purpose: 'register' | 'reset' }) {
  const siteCopy = getSiteCopy(location.locale);
  const copy = siteCopy.account;
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [mismatch, setMismatch] = useState(false);
  const signIn = useSignIn(replacePage);
  const turnstile = useTurnstile(location.locale);
  const start = useMutation({
    mutationFn: (token: string) =>
      postAccount(
        purpose === 'register' ? 'register/start' : 'password/reset/start',
        { email, turnstile_token: token }
      ),
    onSuccess: () => setSent(true),
    onSettled: turnstile.reset
  });
  const finish = useMutation({
    mutationFn: () =>
      postAccount(
        purpose === 'register' ? 'register/confirm' : 'password/reset/confirm',
        { email, code, password }
      ),
    onSuccess: signIn
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!sent) {
      if (turnstile.token) start.mutate(turnstile.token);
      return;
    }
    setMismatch(password !== confirm);
    if (password === confirm) finish.mutate();
  }

  const active = sent ? finish : start;
  const error = mismatch
    ? copy.errors.password_mismatch
    : active.isError
      ? errorMessage(location.locale, active.error)
      : !sent && turnstile.unavailable
        ? copy.errors.captcha_unavailable
        : null;
  return (
    <AuthCard
      location={location}
      title={siteCopy.pageTitles[purpose]}
      links={[
        { href: buildRouteHref('/login', location.locale), label: copy.login }
      ]}
    >
      <form className="flex flex-col gap-4" onSubmit={submit}>
        {sent ? (
          <>
            <div className="flex items-center justify-between gap-3">
              <span className="min-w-0 truncate text-body-medium">{email}</span>
              <Button
                size="sm"
                variant="ghost"
                disabled={finish.isPending}
                onClick={() => {
                  setSent(false);
                  setCode('');
                  setMismatch(false);
                  finish.reset();
                }}
              >
                {copy.changeEmail}
              </Button>
            </div>
            <p role="status" className="text-small text-text-2">
              {fillCopy(
                purpose === 'register' ? copy.registerSent : copy.resetSent,
                { email }
              )}
            </p>
            <Field
              label={copy.code}
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]{6}"
              maxLength={6}
              required
              value={code}
              onChange={(e) => setCode(e.target.value.trim())}
            />
            <Field
              label={purpose === 'register' ? copy.password : copy.newPassword}
              type="password"
              autoComplete="new-password"
              minLength={8}
              maxLength={128}
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <Field
              label={copy.confirmPassword}
              type="password"
              autoComplete="new-password"
              required
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
          </>
        ) : (
          <Field
            label={copy.email}
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        )}
        {sent ? null : turnstile.widget}
        <FormError>{error}</FormError>
        <Button
          type="submit"
          variant="primary"
          busy={active.isPending}
          disabled={!sent && !turnstile.token}
        >
          {!sent
            ? copy.sendCode
            : purpose === 'register'
              ? copy.register
              : copy.savePassword}
        </Button>
      </form>
    </AuthCard>
  );
}

export default function AuthPage(props: Props) {
  const page = props.location.route.page;
  if (page === 'register' || page === 'reset') {
    return <CodeFlow key={page} {...props} purpose={page} />;
  }
  return <Login {...props} />;
}
