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
import { useRouteScroll } from './useRouteScroll';

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
  const mainRef = useRouteScroll();
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
  }, [showToast, t, updater.phase, updater.problem, updater.checkNow]);

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
    <div className="bpp-app">
      <ShellHeader
        app={app}
        bilibiliTriggerRef={bilibiliTriggerRef}
        showBilibili={showBilibili}
        onToggleBilibili={() => {
          setShowBilibili((open) => !open);
          setShowSupport(false);
        }}
        onCloseBilibili={() => setShowBilibili(false)}
      />

      <div className="bpp-shell-body">
        <ShellNavRail
          bootstrap={app.bootstrap}
          supportTriggerRef={supportTriggerRef}
          showSupport={showSupport}
          onToggleSupport={() => {
            setShowSupport((open) => !open);
            setShowBilibili(false);
          }}
          onOpenPayment={() => {
            setShowSupport(false);
            setShowPaymentModal(true);
          }}
          onCloseSupport={() => setShowSupport(false)}
        />
        <main ref={mainRef} tabIndex={-1} className="bpp-main">
          <div className="bpp-main-inner">
            <RouteOutlet />
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

/** Route changes are immediate; the keyed wrapper only replays a short,
 *  non-blocking fade so the new page settles in. */
function RouteOutlet() {
  const location = useLocation();
  return (
    <div key={location.key} className="bpp-route-page">
      <Outlet />
    </div>
  );
}
