import { Outlet, useLocation } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import {
  AppBootstrapProvider,
  useAppBootstrap
} from '../features/about/AppBootstrapProvider';
import { UpdaterProvider, useUpdater } from '../features/about/UpdaterProvider';
import { getUpdaterUiContract } from '../features/about/updaterPresentation';
import { ShellHeader } from './ShellHeader';
import { ShellNavRail } from './ShellNavRail';
import { ShellPaymentModal } from './ShellPaymentModal';
import { ShellUpdateModal } from './ShellUpdateModal';
import {
  ModalCoordinatorProvider,
  ModalSource
} from '../components/ui/ModalCoordinator';
import { ToastProvider, useToast } from '../components/ui/Toast';
import { presentUpdaterProblem } from '../features/about/updaterProblems';
import { useI18n } from '../i18n/LocaleProvider';

export default function GlobalShell() {
  return (
    <AppBootstrapProvider>
      <UpdaterProvider>
        <ToastProvider>
          <ModalCoordinatorProvider>
            <GlobalShellContent />
          </ModalCoordinatorProvider>
        </ToastProvider>
      </UpdaterProvider>
    </AppBootstrapProvider>
  );
}

function GlobalShellContent() {
  const [showBilibili, setShowBilibili] = useState(false);
  const [showSupport, setShowSupport] = useState(false);
  const [showPaymentModal, setShowPaymentModal] = useState(false);
  const bilibiliTriggerRef = useRef<HTMLButtonElement>(null);
  const supportTriggerRef = useRef<HTMLButtonElement>(null);
  const app = useAppBootstrap();
  const updater = useUpdater();
  const { t } = useI18n();
  const { showToast } = useToast();
  const updaterUi = getUpdaterUiContract(updater);
  const previousUpdaterPhase = useRef(updater.phase);

  useEffect(() => {
    const changed = previousUpdaterPhase.current !== updater.phase;
    previousUpdaterPhase.current = updater.phase;
    if (!changed) return;

    if (updater.phase === 'current') {
      showToast({
        id: 'updater:check',
        tone: 'success',
        message: t('updaterCurrent')
      });
    } else if (updater.phase === 'preview') {
      showToast({
        id: 'updater:check',
        tone: 'info',
        message: t('updaterPreview')
      });
    } else if (
      updater.phase === 'failed' &&
      updater.problem.code === 'updater_check_failed'
    ) {
      showToast({
        id: 'updater:check',
        tone: 'error',
        message: presentUpdaterProblem(updater.problem, t),
        action: { label: t('retry'), onClick: updater.checkNow }
      });
    }
  }, [showToast, t, updater]);

  // Close the header popovers on Escape or a click outside them — the native
  // behaviour these controlled dropdowns were missing.
  useEffect(() => {
    if (!showBilibili && !showSupport) return;
    const closeMenus = (restoreFocus = false) => {
      const focusTarget = showBilibili
        ? bilibiliTriggerRef.current
        : supportTriggerRef.current;
      setShowBilibili(false);
      setShowSupport(false);
      if (restoreFocus) queueMicrotask(() => focusTarget?.focus());
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeMenus(true);
    };
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as HTMLElement | null;
      if (!target?.closest('[data-dropdown]')) closeMenus();
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('pointerdown', onPointerDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('pointerdown', onPointerDown);
    };
  }, [showBilibili, showSupport]);

  return (
    <div className="bpp-app flex flex-col">
      <div className="bpp-app-vignette" aria-hidden="true" />
      <ShellHeader
        app={app}
        bilibiliTriggerRef={bilibiliTriggerRef}
        showBilibili={showBilibili}
        onToggleBilibili={() => {
          setShowBilibili((open) => !open);
          setShowSupport(false);
        }}
        showSupport={showSupport}
        supportTriggerRef={supportTriggerRef}
        onToggleSupport={() => {
          setShowSupport((open) => !open);
          setShowBilibili(false);
        }}
        onOpenPayment={() => {
          setShowSupport(false);
          setShowPaymentModal(true);
        }}
        onCloseBilibili={() => setShowBilibili(false)}
        onCloseSupport={() => setShowSupport(false)}
      />

      <div className="bpp-shell-body">
        <ShellNavRail />
        <main tabIndex={-1} className="bpp-main custom-scrollbar">
          <div className="bpp-main-inner">
            <AnimatedOutlet />
          </div>
        </main>
      </div>

      <ModalSource
        id="shell:payment"
        open={showPaymentModal}
        priority="informational"
        dismissalPolicy="dismissible"
        restoreFocusRef={supportTriggerRef}
      >
        <ShellPaymentModal onClose={() => setShowPaymentModal(false)} />
      </ModalSource>
      <ModalSource
        id="shell:update"
        open={updaterUi.modal !== null}
        priority={updaterUi.modal?.priority ?? 'system'}
        dismissalPolicy={updaterUi.modal?.dismissalPolicy ?? 'dismissible'}
      >
        {updaterUi.modal && (
          <ShellUpdateModal updater={updater} presentation={updaterUi.modal} />
        )}
      </ModalSource>
    </div>
  );
}

function primaryPageIndex(pathname: string): number {
  if (pathname.startsWith('/history')) return 1;
  if (pathname.startsWith('/stream')) return 2;
  if (pathname.startsWith('/about')) return 3;
  return 0;
}

function AnimatedOutlet() {
  const location = useLocation();
  const currentIndex = primaryPageIndex(location.pathname);
  const previousIndex = useRef(currentIndex);
  const direction =
    currentIndex > previousIndex.current
      ? 'forward'
      : currentIndex < previousIndex.current
        ? 'backward'
        : 'neutral';

  useEffect(() => {
    previousIndex.current = currentIndex;
  }, [currentIndex]);

  return (
    <div
      key={location.key}
      className={`bpp-route-page is-${direction}`}
      data-route-index={currentIndex}
    >
      <Outlet />
    </div>
  );
}
