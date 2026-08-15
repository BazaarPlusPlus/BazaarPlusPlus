import type { AppBootstrap } from '../../types/backend';
import {
  aboutBootstrapProblemFromError,
  type AboutBootstrapProblem
} from './aboutProblems';

export type AppBootstrapLoadResult = {
  source: 'native' | 'preview';
  data: AppBootstrap;
};

export type AppBootstrapUnavailableField = 'bundled_bpp_version';

type AppBootstrapAvailability = {
  unavailableFields: readonly AppBootstrapUnavailableField[];
};

export type AppBootstrapSnapshot =
  | (AppBootstrapAvailability & {
      phase: 'initial-loading';
      data: AppBootstrap;
      source: 'packaged-fallback';
      problem: null;
      retrying: false;
    })
  | {
      phase: 'initial-loading';
      data: null;
      source: null;
      unavailableFields: readonly [];
      problem: null;
      retrying: false;
    }
  | (AppBootstrapAvailability & {
      phase: 'authoritative';
      data: AppBootstrap;
      source: 'native';
      problem: null;
      retrying: false;
    })
  | (AppBootstrapAvailability & {
      phase: 'fallback';
      data: AppBootstrap;
      source: 'packaged-fallback';
      problem: AboutBootstrapProblem | null;
      retrying: boolean;
    })
  | {
      phase: 'blocking-failure';
      data: null;
      source: null;
      unavailableFields: readonly [];
      problem: AboutBootstrapProblem;
      retrying: boolean;
    };

export type AppBootstrapImpl = {
  fallback: AppBootstrap | null;
  load: () => Promise<AppBootstrapLoadResult>;
};

export type AppBootstrapMachine = {
  getSnapshot: () => AppBootstrapSnapshot;
  start: () => Promise<void>;
  retry: () => Promise<void>;
};

export function createInitialAppBootstrapSnapshot(
  fallback: AppBootstrap | null
): AppBootstrapSnapshot {
  return fallback
    ? {
        phase: 'initial-loading',
        data: fallback,
        source: 'packaged-fallback',
        unavailableFields: unavailableFields(fallback),
        problem: null,
        retrying: false
      }
    : {
        phase: 'initial-loading',
        data: null,
        source: null,
        unavailableFields: [],
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
    if (result.source === 'native') {
      publish({
        phase: 'authoritative',
        data: result.data,
        source: 'native',
        unavailableFields: unavailableFields(result.data),
        problem: null,
        retrying: false
      });
      return;
    }

    publish({
      phase: 'fallback',
      data: result.data,
      source: 'packaged-fallback',
      unavailableFields: unavailableFields(result.data),
      problem: null,
      retrying: false
    });
  };

  const fail = (error: unknown, usableData: AppBootstrap | null) => {
    const problem = aboutBootstrapProblemFromError(error);
    if (usableData) {
      publish({
        phase: 'fallback',
        data: usableData,
        source: 'packaged-fallback',
        unavailableFields: unavailableFields(usableData),
        problem,
        retrying: false
      });
      return;
    }

    publish({
      phase: 'blocking-failure',
      data: null,
      source: null,
      unavailableFields: [],
      problem,
      retrying: false
    });
  };

  const request = async (usableData: AppBootstrap | null) => {
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
    if (
      requestInFlight ||
      (snapshot.phase !== 'fallback' && snapshot.phase !== 'blocking-failure')
    ) {
      return Promise.resolve();
    }
    const usableData = snapshot.data;
    publish({ ...snapshot, retrying: true });
    return request(usableData);
  };

  return { getSnapshot: () => snapshot, start, retry };
}

function unavailableFields(
  data: AppBootstrap | null
): readonly AppBootstrapUnavailableField[] {
  if (!data) return [];
  return data.bundled_bpp_version == null ? ['bundled_bpp_version'] : [];
}
