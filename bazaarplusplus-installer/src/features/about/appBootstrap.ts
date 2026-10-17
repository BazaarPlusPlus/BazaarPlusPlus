import type { AppBootstrap } from '../../types/backend';
import {
  aboutBootstrapProblemFromError,
  type AboutBootstrapProblem
} from './aboutProblems';

export type AppBootstrapLoadResult = {
  source: 'native' | 'preview';
  data: AppBootstrap;
};

export type AppBootstrapSnapshot =
  | {
      phase: 'initial-loading';
      data: AppBootstrap;
      problem: null;
      retrying: false;
    }
  | {
      phase: 'authoritative';
      data: AppBootstrap;
      problem: null;
      retrying: false;
    }
  | {
      phase: 'fallback';
      data: AppBootstrap;
      problem: AboutBootstrapProblem | null;
      retrying: boolean;
    };

export type AppBootstrapImpl = {
  fallback: AppBootstrap;
  load: () => Promise<AppBootstrapLoadResult>;
};

export type AppBootstrapMachine = {
  getSnapshot: () => AppBootstrapSnapshot;
  start: () => Promise<void>;
  retry: () => Promise<void>;
};

export function createInitialAppBootstrapSnapshot(
  fallback: AppBootstrap
): AppBootstrapSnapshot {
  return {
    phase: 'initial-loading',
    data: fallback,
    problem: null,
    retrying: false
  };
}

export function createAppBootstrapMachine(
  impl: AppBootstrapImpl,
  onChange: (snapshot: AppBootstrapSnapshot) => void = () => undefined
): AppBootstrapMachine {
  let snapshot = createInitialAppBootstrapSnapshot(impl.fallback);
  let requestInFlight = false;

  const publish = (next: AppBootstrapSnapshot) => {
    snapshot = next;
    onChange(snapshot);
  };

  const complete = (result: AppBootstrapLoadResult) => {
    publish({
      phase: result.source === 'native' ? 'authoritative' : 'fallback',
      data: result.data,
      problem: null,
      retrying: false
    });
  };

  const fail = (error: unknown, usableData: AppBootstrap) => {
    publish({
      phase: 'fallback',
      data: usableData,
      problem: aboutBootstrapProblemFromError(error),
      retrying: false
    });
  };

  const request = async (usableData: AppBootstrap) => {
    if (requestInFlight) return;
    requestInFlight = true;
    try {
      complete(await impl.load());
    } catch (error) {
      fail(error, usableData);
    } finally {
      requestInFlight = false;
    }
  };

  const start = () => {
    if (snapshot.phase !== 'initial-loading') return Promise.resolve();
    return request(snapshot.data);
  };

  const retry = () => {
    if (requestInFlight || snapshot.phase !== 'fallback') {
      return Promise.resolve();
    }
    const usableData = snapshot.data;
    publish({ ...snapshot, retrying: true });
    return request(usableData);
  };

  return { getSnapshot: () => snapshot, start, retry };
}
