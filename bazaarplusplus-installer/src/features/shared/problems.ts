import type { SemanticProblem } from '../../types/backend';
import { semanticProblemFromError } from '../../api/problems';

export type UiProblem<Code extends string = string> = Omit<
  SemanticProblem,
  'code'
> & {
  code: Code;
};

export function createUiProblem<Code extends string>(
  code: Code,
  options?: {
    params?: Record<string, string>;
    diagnostic?: string | null;
  }
): UiProblem<Code> {
  return {
    code,
    params: options?.params ?? {},
    diagnostic: options?.diagnostic ?? null
  };
}

export function problemFromError<Code extends string>(
  error: unknown,
  fallbackCode: Code
): UiProblem<SemanticProblem['code'] | Code> {
  const semantic = semanticProblemFromError(error);
  if (semantic) {
    return semantic;
  }
  return createUiProblem(fallbackCode, { diagnostic: diagnosticFrom(error) });
}

export function formatProblemDiagnostic(problem: UiProblem): string {
  const params = Object.entries(problem.params)
    .map(([name, value]) => `${name}=${value}`)
    .join(' ');
  return [problem.code, params, problem.diagnostic].filter(Boolean).join('\n');
}

function diagnosticFrom(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === 'string') {
    return error;
  }
  return 'Unknown command failure';
}
