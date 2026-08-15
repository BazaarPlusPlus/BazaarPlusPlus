import { commands as generatedCommands } from '../types/generated/commands';
import type { CommandAdapter } from './commandAdapter';
import { isSemanticProblem, SemanticProblemError } from './problems';

export function normalizeBackendError(error: unknown): Error {
  if (error instanceof Error) {
    return error;
  }
  if (isSemanticProblem(error)) {
    return new SemanticProblemError(error);
  }
  if (typeof error === 'string') {
    return new Error(error);
  }
  return new Error('Backend command failed.');
}

function withNormalizedErrors(
  client: typeof generatedCommands
): CommandAdapter {
  return new Proxy(client, {
    get(target, property, receiver) {
      const command = Reflect.get(target, property, receiver);
      if (typeof command !== 'function') return command;

      return async (...args: unknown[]) => {
        try {
          return await command(...args);
        } catch (error) {
          throw normalizeBackendError(error);
        }
      };
    }
  }) as CommandAdapter;
}

export const nativeCommands = withNormalizedErrors(generatedCommands);
