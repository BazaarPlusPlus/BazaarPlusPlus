import { hasTauriRuntime } from './runtime';
import { nativeCommands } from './nativeCommands';
import { createPreviewCommands } from './previewCommands';

const previewCommands = createPreviewCommands(nativeCommands);

/** Runtime selection happens once; feature modules only see this semantic client. */
export const commandClient = hasTauriRuntime()
  ? nativeCommands
  : previewCommands;
