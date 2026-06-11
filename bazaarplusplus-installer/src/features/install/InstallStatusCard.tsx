import { AlertCircle, CheckCircle2 } from 'lucide-react';

export function InstallStatusCard({
  title,
  detail,
  label,
  tone
}: {
  title: string;
  detail: string;
  label: string;
  tone: 'ok' | 'warn';
}) {
  return (
    <div className="min-w-0 p-4 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex items-center justify-between gap-4">
      <div className="min-w-0">
        <h3 className="cinzel font-bold text-[#e8dcc8]">{title}</h3>
        <p
          className="fira-code text-xs text-[rgba(200,170,120,0.8)] mt-1 truncate selectable"
          title={detail}
        >
          {detail}
        </p>
      </div>
      <div
        className={`flex items-center gap-2 px-3 py-1 rounded-sm border text-xs shrink-0 ${tone === 'ok' ? 'text-[#6dd9a0] bg-[rgba(80,180,120,0.15)] border-[rgba(80,180,120,0.25)]' : 'text-[rgba(232,190,120,0.9)] bg-[rgba(200,148,55,0.12)] border-[rgba(200,148,55,0.24)]'}`}
      >
        {tone === 'ok' ? <CheckCircle2 size={14} /> : <AlertCircle size={14} />}
        <span>{label}</span>
      </div>
    </div>
  );
}
