import { describe, expect, it } from 'vite-plus/test';
import { formatMessage, type MessageKey } from '../../i18n/messages';
import { createUiProblem } from '../shared/problems';
import {
  installFailurePaths,
  presentInstallProblem,
  presentInstallWarning
} from './installProblems';

const warnings = [
  [{ code: 'game_missing', params: {} }, 'installWarningGameMissing'],
  [
    { code: 'steam_config_unavailable', params: {} },
    'installWarningSteamConfigUnavailable'
  ],
  [
    { code: 'launch_options_not_empty', params: {} },
    'installWarningLaunchOptionsNotEmpty'
  ],
  [
    { code: 'trampoline_not_ready', params: {} },
    'installWarningTrampolineNotReady'
  ],
  [
    { code: 'obsolete_macos_artifacts', params: {} },
    'installWarningObsoleteMacosArtifacts'
  ]
] as const satisfies readonly (readonly [unknown, MessageKey])[];

const problems = [
  [
    createUiProblem('install_detection_failed', {
      params: { operation: 'detect_state' }
    }),
    'installProblemDetectionFailed'
  ],
  [
    createUiProblem('install_action_failed', {
      params: { operation: 'choose_directory' }
    }),
    'installProblemChooseDirectoryFailed'
  ],
  [
    createUiProblem('install_action_failed', {
      params: { operation: 'install' }
    }),
    'installProblemInstallFailed'
  ],
  [
    createUiProblem('install_action_failed', {
      params: { operation: 'uninstall' }
    }),
    'installProblemUninstallFailed'
  ],
  [
    createUiProblem('install_action_failed', {
      params: { operation: 'launch' }
    }),
    'installProblemLaunchFailed'
  ],
  [createUiProblem('install_blocked_by_game'), 'installBlockedByGame'],
  [createUiProblem('reset_blocked_by_game'), 'resetDataBlockedByGame'],
  [
    createUiProblem('bepinex_reset_blocked_by_game'),
    'resetBepinexBlockedByGame'
  ],
  [
    createUiProblem('legacy_delete_blocked_by_game'),
    'legacyDeleteBlockedByGame'
  ],
  [
    createUiProblem('install_partial_failure', {
      params: { operation: 'reset_bpp_data', count: '2' }
    }),
    'resetDataPartialFailure'
  ],
  [
    createUiProblem('install_partial_failure', {
      params: { operation: 'reset_bepinex', count: '2' }
    }),
    'resetBepinexPartialFailure'
  ],
  [
    createUiProblem('install_action_failed', {
      params: { operation: 'get_legacy_data_state' }
    }),
    'installProblemLegacyDataFailed'
  ],
  [
    createUiProblem('install_action_failed', {
      params: { operation: 'delete_legacy_root' }
    }),
    'installProblemLegacyDeleteFailed'
  ],
  [
    createUiProblem('install_partial_failure', {
      params: { operation: 'delete_legacy_root', count: '2' }
    }),
    'legacyDeletePartialFailure'
  ],
  [createUiProblem('install_unexpected'), 'installProblemUnexpected']
] as const satisfies readonly (readonly [unknown, MessageKey])[];

describe('Install semantic presentation', () => {
  it.each(warnings)(
    'localizes warning %o with %s in Chinese and English',
    (warning, key) => {
      for (const locale of ['zh', 'en'] as const) {
        expect(
          presentInstallWarning(warning, (k, params) =>
            formatMessage(locale, k, params)
          )
        ).toBe(formatMessage(locale, key));
      }
    }
  );

  it.each(problems)(
    'localizes problem %o with %s in Chinese and English',
    (problem, key) => {
      for (const locale of ['zh', 'en'] as const) {
        expect(
          presentInstallProblem(problem, (k, params) =>
            formatMessage(locale, k, params)
          )
        ).toBe(formatMessage(locale, key, problem.params));
      }
    }
  );

  it('recovers partial-failure paths from semantic parameters', () => {
    expect(
      installFailurePaths(
        createUiProblem('install_partial_failure', {
          params: { paths: '/tmp/a\u001f/tmp/b', count: '2' }
        })
      )
    ).toEqual(['/tmp/a', '/tmp/b']);
  });
});
