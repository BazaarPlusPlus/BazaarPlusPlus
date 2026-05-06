export function buildStreamCommandArgs<T extends Record<string, unknown>>(
  gamePath: string | null | undefined,
  extraArgs: T
): T & { gamePath?: string } {
  const normalizedGamePath = gamePath?.trim();
  if (!normalizedGamePath) {
    return { ...extraArgs };
  }

  return {
    ...extraArgs,
    gamePath: normalizedGamePath
  };
}
