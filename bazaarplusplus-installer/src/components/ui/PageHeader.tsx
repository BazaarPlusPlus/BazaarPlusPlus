import type { ReactNode } from 'react';

export function PageHeader({
  eyebrow,
  title,
  action
}: {
  eyebrow: string;
  title: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1 shrink-0">
      <p className="cinzel text-[10px] tracking-widest text-[rgba(200,148,55,0.75)] uppercase">
        {eyebrow}
      </p>
      {action ? (
        <div className="flex items-center justify-between">
          <Title>{title}</Title>
          {action}
        </div>
      ) : (
        <Title>{title}</Title>
      )}
    </div>
  );
}

function Title({ children }: { children: ReactNode }) {
  return (
    <h2 className="cinzel text-lg tracking-wider text-[rgba(232,220,194,0.92)] uppercase m-0">
      {children}
    </h2>
  );
}
