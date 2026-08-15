import type { commands as generatedCommands } from '../types/generated/commands';

/** Shared contract implemented by both the generated native client and Preview. */
export type CommandAdapter = typeof generatedCommands;
