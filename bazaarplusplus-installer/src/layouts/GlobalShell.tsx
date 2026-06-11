import { Outlet } from 'react-router-dom';
import { useEffect, useState } from 'react';
import {
  AppBootstrapProvider,
  useAppBootstrap
} from '../features/about/AppBootstrapProvider';
import { UpdaterProvider, useUpdater } from '../features/about/UpdaterProvider';
import { isUpdateModalPhase } from '../features/about/updater';
import { ShellHeader } from './ShellHeader';
import { ShellNavRail } from './ShellNavRail';
import { ShellPaymentModal } from './ShellPaymentModal';
import { ShellUpdateModal } from './ShellUpdateModal';

export default function GlobalShell() {
  return (
    <AppBootstrapProvider>
      <UpdaterProvider>
        <GlobalShellContent />
      </UpdaterProvider>
    </AppBootstrapProvider>
  );
}

function GlobalShellContent() {
  const [showBilibili, setShowBilibili] = useState(false);
  const [showSupport, setShowSupport] = useState(false);
  const [showPaymentModal, setShowPaymentModal] = useState(false);
  const app = useAppBootstrap();
  const updater = useUpdater();

  // Close the header popovers on Escape or a click outside them — the native
  // behaviour these controlled dropdowns were missing.
  useEffect(() => {
    if (!showBilibili && !showSupport) return;
    const closeMenus = () => {
      setShowBilibili(false);
      setShowSupport(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeMenus();
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
    <div className="flex flex-col h-full bg-[#0b0906] text-[#e8dcc8]">
      <ShellHeader
        app={app}
        showBilibili={showBilibili}
        onToggleBilibili={() => {
          setShowBilibili((open) => !open);
          setShowSupport(false);
        }}
        showSupport={showSupport}
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

      <div className="flex-1 flex overflow-hidden">
        <ShellNavRail />
        <main className="flex-1 overflow-y-auto bg-transparent relative">
          <div
            className="absolute inset-0 pointer-events-none opacity-5"
            style={{
              backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='300' height='300'%3E%3Cfilter id='g'%3E%3CfeTurbulence type='turbulence' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='300' height='300' filter='url(%23g)'/%3E%3C/svg%3E")`,
              backgroundRepeat: 'repeat'
            }}
          />
          <div className="p-8 h-full w-full relative z-10">
            <Outlet />
          </div>
        </main>
      </div>

      {showPaymentModal && (
        <ShellPaymentModal onClose={() => setShowPaymentModal(false)} />
      )}
      {isUpdateModalPhase(updater) && <ShellUpdateModal updater={updater} />}
    </div>
  );
}
