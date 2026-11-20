import { hasTauriRuntime } from './runtime';
import { nativeCommands } from './nativeCommands';
import { createPreviewCommands } from './previewCommands';

export const commandClient = hasTauriRuntime()
  ? nativeCommands
  : createPreviewCommands(nativeCommands);
