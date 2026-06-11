import { FolderOpen } from 'lucide-react';
import type { useInstallPage } from './useInstallPage';
import { InstallStatusCard } from './InstallStatusCard';
import { useI18n } from '../../i18n/LocaleProvider';

type InstallPage = ReturnType<typeof useInstallPage>;

export function InstallStatusPanel({ page }: { page: InstallPage }) {
  const { t } = useI18n();
  return (
    <div className="col-span-7 flex flex-col gap-6">
      <div className="p-5 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex flex-col gap-6 h-full">
        <section>
          <h3 className="cinzel text-xs tracking-widest text-[rgba(220,195,145,0.8)] mb-3 uppercase">
            {t('currentStatusHeading')}
          </h3>
          <div className="grid gap-3">
            <InstallStatusCard
              title="The Bazaar"
              detail={
                page.state.game.display_version ??
                page.state.selected_game_path ??
                '-'
              }
              label={page.status.gameLabel}
              tone={page.status.gameTone}
            />
            <InstallStatusCard
              title="BazaarPlusPlus"
              detail={page.status.modVersion}
              label={page.status.modLabel}
              tone={page.status.modTone}
            />
          </div>
        </section>

        <section className="mt-auto">
          <h3 className="cinzel text-xs tracking-widest text-[rgba(220,195,145,0.8)] mb-3 uppercase">
            {t('gamePathHeading')}
          </h3>
          <div className="p-4 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)]">
            <div className="flex items-center gap-3 fira-code text-sm text-[#e8dcc8] mb-4 overflow-hidden">
              <FolderOpen
                size={16}
                className="text-[rgba(200,170,120,0.8)] shrink-0"
              />
              <span
                className="truncate"
                title={page.state.selected_game_path ?? t('notSelected')}
              >
                {page.state.selected_game_path ?? t('gamePathEmpty')}
              </span>
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={page.busy}
                onClick={page.chooseDirectory}
                className="px-4 py-1.5 text-xs bg-[rgba(200,148,55,0.04)] border border-[rgba(180,130,48,0.2)] rounded-sm hover:bg-[rgba(200,148,55,0.1)] disabled:opacity-40 transition-colors text-[#e8dcc8]"
              >
                {t('chooseAgain')}
              </button>
              <button
                type="button"
                disabled={page.busy}
                onClick={() => page.refresh()}
                className="px-4 py-1.5 text-xs bg-[rgba(200,148,55,0.04)] border border-[rgba(180,130,48,0.2)] rounded-sm hover:bg-[rgba(200,148,55,0.1)] disabled:opacity-40 transition-colors text-[#e8dcc8]"
              >
                {t('recheck')}
              </button>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
