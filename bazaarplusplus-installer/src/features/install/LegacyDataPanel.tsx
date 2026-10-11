import { FolderClock, HardDriveDownload, Loader2 } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { StatusBanner } from '../../components/ui/StatusBanner';
import { useI18n } from '../../i18n/LocaleProvider';
import { formatBytes } from '../history/format';
import { InstallProblemBanner } from './InstallProblemBanner';
import type {
  LegacyDataSnapshot,
  LegacyDataWorkflow
} from './legacyDataWorkflow';

/**
 * Folders earlier major versions wrote into the game directory. Each one is
 * deleted only after its own confirmation; the installed-mod hint asks for a
 * reinstall when the mod on disk still writes one of them, and only then is
 * the V5 import card replaced by that hint. `LEGACY_DATA_SECTION_ID` is the
 * anchor History links to when it has no current database.
 */
export const LEGACY_DATA_SECTION_ID = 'legacy-data';

export function LegacyDataPanel({
  snapshot,
  workflow,
  modInstalled,
  gamePath
}: {
  snapshot: LegacyDataSnapshot;
  workflow: LegacyDataWorkflow;
  modInstalled: boolean;
  gamePath: string | null;
}) {
  const { t, locale } = useI18n();
  const load = snapshot.load;

  if (load.phase === 'failed') {
    return (
      <InstallProblemBanner
        problem={load.problem}
        onRetry={() => void workflow.load(gamePath)}
      />
    );
  }
  const data = load.phase === 'idle' ? null : load.data;
  // Without a game directory there is nothing to list or reinstall into.
  if (!data?.game_path) return null;

  const reinstallRequired =
    modInstalled && data.installed_mod_data_root === 'reinstall_required';
  if (data.roots.length === 0 && !reinstallRequired) return null;

  const deleting =
    snapshot.confirmation?.phase === 'running'
      ? snapshot.confirmation.target.name
      : null;
  const importRun = snapshot.importRun;
  const importing = importRun.phase === 'running';
  const showImport =
    !reinstallRequired && (data.v5_import_eligible || importing);

  return (
    <section
      id={LEGACY_DATA_SECTION_ID}
      className="bpp-panel"
      aria-labelledby="legacy-data-heading"
    >
      <div className="bpp-row">
        <FolderClock size={16} aria-hidden="true" />
        <span className="bpp-row-copy">
          <span id="legacy-data-heading" className="bpp-row-title">
            {t('legacyDataHeading')}
          </span>
          <span className="bpp-row-description">{t('legacyDataHint')}</span>
        </span>
      </div>
      {reinstallRequired && (
        <div className="px-4 py-3">
          <StatusBanner
            tone="warning"
            message={t('legacyModReinstallRequired')}
          />
        </div>
      )}
      {showImport && (
        <div className="bpp-row" aria-busy={importing}>
          <HardDriveDownload size={16} aria-hidden="true" />
          <div className="bpp-row-copy">
            <span className="bpp-row-title">{t('legacyImportHeading')}</span>
            <span className="bpp-row-description">{t('legacyImportHint')}</span>
            {importing && (
              <p role="status" aria-live="polite" className="bpp-inline-status">
                <Loader2 size={13} className="bpp-spin" aria-hidden="true" />
                {t('legacyImportRunning')}
              </p>
            )}
          </div>
          <Button
            size="sm"
            disabled={snapshot.confirmation !== null}
            busy={importing}
            onClick={() => void workflow.importV5()}
          >
            {t('legacyImportAction')}
          </Button>
        </div>
      )}
      {showImport && importRun.phase === 'failed' && (
        <div className="px-4 py-3">
          <InstallProblemBanner
            problem={importRun.problem}
            onRetry={() => void workflow.importV5()}
          />
        </div>
      )}
      {data.roots.map((root) => (
        <div className="bpp-row" key={root.name}>
          <div className="bpp-row-copy">
            <span className="bpp-row-title font-mono">{root.name}</span>
            <span className="bpp-row-description">
              {t('legacyRootSize', {
                size: formatBytes(root.size_bytes, locale),
                files: root.file_count
              })}
            </span>
            {root.hard_linked_file_count > 0 && (
              <span className="bpp-row-description">
                {t('legacyRootHardLinked', {
                  count: root.hard_linked_file_count
                })}
              </span>
            )}
            {root.unreadable_entry_count > 0 && (
              <span className="bpp-row-description">
                {t('legacyRootUnreadable', {
                  count: root.unreadable_entry_count
                })}
              </span>
            )}
          </div>
          <Button
            variant="danger"
            size="sm"
            disabled={snapshot.confirmation !== null || importing}
            busy={deleting === root.name}
            onClick={() => workflow.requestDelete(root.name)}
            aria-label={t('legacyDeleteAction', { name: root.name })}
          >
            {t('actionDelete')}
          </Button>
        </div>
      ))}
    </section>
  );
}
