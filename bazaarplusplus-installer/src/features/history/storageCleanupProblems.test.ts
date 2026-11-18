import { describe, expect, it } from 'vite-plus/test';
import { formatMessage } from '../../i18n/messages';
import {
  presentStorageCleanupProblem,
  storageCleanupProblemFromError
} from './storageCleanupProblems';

describe('storage cleanup problems', () => {
  it.each([
    ['preview_storage_cleanup', '无法预览清理范围，请重试。'],
    [
      'execute_storage_cleanup',
      '清理未完成，目标和范围已保留；请确认游戏已关闭，并查看诊断后重试或安全关闭。'
    ]
  ])('presents a failed %s with its own copy', (operation, copy) => {
    const input = {
      code: 'history_action_failed',
      params: { operation },
      diagnostic: 'database is locked'
    };
    const problem = storageCleanupProblemFromError(input);

    expect(problem).toEqual(input);
    expect(
      presentStorageCleanupProblem(problem, (key, params) =>
        formatMessage('zh', key, params)
      )
    ).toBe(copy);
  });
});
