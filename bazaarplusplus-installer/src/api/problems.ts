import type { SemanticProblem } from '../types/backend';

const semanticProblemCodes: Record<SemanticProblem['code'], true> = {
  history_unavailable: true,
  history_read_failed: true,
  history_read_blocked_by_game: true,
  history_database_unsupported_schema: true,
  history_action_failed: true,
  install_detection_failed: true,
  install_action_failed: true,
  install_game_running: true,
  install_partial_failure: true,
  stream_service_failed: true,
  stream_window_failed: true,
  stream_crop_failed: true
};

export class SemanticProblemError extends Error {
  readonly problem: SemanticProblem;

  constructor(problem: SemanticProblem) {
    super(problem.code);
    this.name = 'SemanticProblemError';
    this.problem = problem;
  }
}

export function isSemanticProblem(value: unknown): value is SemanticProblem {
  if (
    !isRecord(value) ||
    typeof value.code !== 'string' ||
    !Object.hasOwn(semanticProblemCodes, value.code)
  ) {
    return false;
  }
  if (!isRecord(value.params)) {
    return false;
  }
  if (!Object.values(value.params).every((item) => typeof item === 'string')) {
    return false;
  }
  return value.diagnostic === null || typeof value.diagnostic === 'string';
}

export function semanticProblemFromError(
  error: unknown
): SemanticProblem | null {
  if (error instanceof SemanticProblemError) {
    return error.problem;
  }
  return isSemanticProblem(error) ? error : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
