import type { ReactNode } from 'react';

/** Small neutral label pill (22px high). Status badges would add a dot + tone here. */
export default function Badge({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex h-[22px] shrink-0 items-center rounded-full bg-hover px-2 text-xs font-medium whitespace-nowrap text-text-2">
      {children}
    </span>
  );
}
