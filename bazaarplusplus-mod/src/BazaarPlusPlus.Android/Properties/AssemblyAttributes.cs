using System.Runtime.CompilerServices;

// AndroidHost.Tests loads the compiled host and runs its managed paths (ADR-0016).
[assembly: InternalsVisibleTo("AndroidHost.Tests")]
