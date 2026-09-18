import { useLayoutEffect, useRef } from 'react';
import { useLocation, useNavigationType } from 'react-router-dom';

export function useRouteScroll() {
  const mainRef = useRef<HTMLElement>(null);
  const positions = useRef(new Map<string, number>());
  const previousPath = useRef('');
  const location = useLocation();
  const navigationType = useNavigationType();
  const path = location.pathname + location.search;

  useLayoutEffect(() => {
    const main = mainRef.current;
    if (!main) return;
    const returningToHistory =
      location.pathname === '/history' &&
      previousPath.current.startsWith('/history/');
    const restore = navigationType === 'POP' || returningToHistory;
    const target = restore ? (positions.current.get(path) ?? 0) : 0;
    previousPath.current = path;
    let pending = target > 0;

    // A returning page can be shorter while its data is loading. Keep the
    // saved offset until content can accommodate it or the user takes over.
    const observer = new ResizeObserver(() => {
      if (pending) applyPosition();
    });
    function applyPosition() {
      main!.scrollTop = target;
      pending = main!.scrollTop < target;
      if (!pending) observer.disconnect();
    }
    const savePosition = () => {
      if (!pending) positions.current.set(path, main.scrollTop);
    };
    const cancelRestore = () => {
      pending = false;
      observer.disconnect();
      savePosition();
    };
    applyPosition();
    savePosition();
    if (pending && main.firstElementChild) {
      observer.observe(main.firstElementChild);
    }
    main.addEventListener('scroll', savePosition);
    main.addEventListener('wheel', cancelRestore, { passive: true });
    main.addEventListener('touchstart', cancelRestore, { passive: true });
    main.addEventListener('pointerdown', cancelRestore);
    main.addEventListener('keydown', cancelRestore);
    return () => {
      observer.disconnect();
      main.removeEventListener('scroll', savePosition);
      main.removeEventListener('wheel', cancelRestore);
      main.removeEventListener('touchstart', cancelRestore);
      main.removeEventListener('pointerdown', cancelRestore);
      main.removeEventListener('keydown', cancelRestore);
    };
  }, [location.key, location.pathname, navigationType, path]);

  return mainRef;
}
