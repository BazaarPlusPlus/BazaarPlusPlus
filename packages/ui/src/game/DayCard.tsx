import type { ReactNode } from 'react';

export function DayCard({
  title,
  battles
}: {
  title: string;
  battles: ReactNode;
}) {
  return (
    <li className="bpp-panel bpp-day-card">
      <h2 className="bpp-day-title">{title}</h2>
      {battles}
    </li>
  );
}
