import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import { createUiProblem } from '../shared/problems';
import {
  installFailurePaths,
  presentInstallProblem,
  presentInstallWarning
} from './installProblems';

const warnings = [
  { code: 'game_missing', params: {} },
  { code: 'steam_config_unavailable', params: {} },
  { code: 'launch_options_not_empty', params: {} },
  { code: 'trampoline_not_ready', params: {} },
  { code: 'obsolete_macos_artifacts', params: {} }
] as const;

const problems = [
  createUiProblem('install_detection_failed', {
    params: { operation: 'detect_state' }
  }),
  createUiProblem('install_action_failed', {
    params: { operation: 'choose_directory' }
  }),
  createUiProblem('install_action_failed', {
    params: { operation: 'install' }
  }),
  createUiProblem('install_action_failed', {
    params: { operation: 'uninstall' }
  }),
  createUiProblem('install_action_failed', {
    params: { operation: 'launch' }
  }),
  createUiProblem('install_game_running', {
    params: { operation: 'reset_bpp_data' }
  }),
  createUiProblem('install_game_running', {
    params: { operation: 'reset_bepinex' }
  }),
  createUiProblem('install_partial_failure', {
    params: { operation: 'reset_bpp_data', count: '2' }
  }),
  createUiProblem('install_partial_failure', {
    params: { operation: 'reset_bepinex', count: '2' }
  }),
  createUiProblem('install_unexpected')
] as const;

describe('Install semantic presentation', () => {
  it.each(warnings)(
    'localizes warning $code in Chinese and English',
    (warning) => {
      const zh = presentInstallWarning(warning, (key, params) =>
        formatMessage('zh', key, params)
      );
      const en = presentInstallWarning(warning, (key, params) =>
        formatMessage('en', key, params)
      );

      expect(zh).not.toBe(warning.code);
      expect(en).not.toBe(warning.code);
      expect(en).not.toBe(zh);
    }
  );

  it.each(problems)(
    'localizes problem $code $params with recovery copy',
    (problem) => {
      const zh = presentInstallProblem(problem, (key, params) =>
        formatMessage('zh', key, params)
      );
      const en = presentInstallProblem(problem, (key, params) =>
        formatMessage('en', key, params)
      );

      expect(zh).not.toBe(problem.code);
      expect(en).not.toBe(problem.code);
      expect(en).not.toBe(zh);
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
