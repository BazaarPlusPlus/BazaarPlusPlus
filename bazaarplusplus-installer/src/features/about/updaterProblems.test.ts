import { describe, expect, it } from 'vitest';
import { formatMessage, type Locale } from '../../i18n/messages';
import { formatProblemDiagnostic } from '../shared/problems';
import {
  presentUpdaterProblem,
  updaterProblemFromError,
  type UpdaterProblemOperation
} from './updaterProblems';

const expected: Record<UpdaterProblemOperation, Record<Locale, string>> = {
  check: {
    zh: '无法检查更新。请检查网络连接后重试。',
    en: 'Could not check for updates. Check your network connection, then retry.'
  },
  download: {
    zh: '更新下载失败。请检查网络连接后重试。',
    en: 'The update could not be downloaded. Check your network connection, then retry.'
  },
  install: {
    zh: '更新安装失败。请重试；如果问题持续，请重新打开应用后再次检查更新。',
    en: 'The update could not be installed. Retry; if the problem continues, reopen the app and check again.'
  },
  restart: {
    zh: '自动重启失败，但 BazaarPlusPlus 5.1.0 已安装完成。请退出 BazaarPlusPlus，再从“应用程序”中重新打开。',
    en: 'Automatic restart failed, but BazaarPlusPlus 5.1.0 is installed. Quit BazaarPlusPlus, then open it again from Applications.'
  }
};

describe('updater semantic problems', () => {
  for (const operation of [
    'check',
    'download',
    'install',
    'restart'
  ] as const) {
    it(`localizes ${operation} recovery without using diagnostics as copy`, () => {
      const problem = updaterProblemFromError(
        new Error('native secret'),
        operation,
        operation === 'restart' ? '5.1.0' : null
      );

      for (const locale of ['zh', 'en'] as const) {
        const presented = presentUpdaterProblem(problem, (key, params) =>
          formatMessage(locale, key, params)
        );
        expect(presented).toBe(expected[operation][locale]);
        expect(presented).not.toContain('native secret');
      }
      expect(formatProblemDiagnostic(problem)).toContain('native secret');
    });
  }
});
