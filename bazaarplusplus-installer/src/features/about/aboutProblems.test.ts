import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import {
  aboutBootstrapProblemFromError,
  presentAboutProblem
} from './aboutProblems';

describe('About bootstrap problem presentation', () => {
  it('keeps diagnostics separate from localized recovery copy', () => {
    const problem = aboutBootstrapProblemFromError(
      new Error('native stack detail')
    );

    expect(problem).toEqual({
      code: 'about_bootstrap_failed',
      params: { operation: 'load_bootstrap' },
      diagnostic: 'native stack detail'
    });
    expect(
      presentAboutProblem(problem, (key, params) =>
        formatMessage('zh', key, params)
      )
    ).toContain('备用数据');
    expect(
      presentAboutProblem(problem, (key, params) =>
        formatMessage('en', key, params)
      )
    ).toContain('fallback');
    expect(
      presentAboutProblem(problem, (key, params) =>
        formatMessage('en', key, params)
      )
    ).not.toContain('native stack detail');
  });
});
