using System;
using UnityEngine;

namespace BazaarPlusPlus.Ios.Helper
{
    // Checks runtime changes the runner makes to Mono against the managed code they replace. Enabled by
    // BPP_SELFTEST=1; logs one line per check.
    public static class SelfTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Start()
        {
            if (Environment.GetEnvironmentVariable("BPP_SELFTEST") != "1") return;
            // The interpreter computes string.GetHashCode natively (MINT_STRHASH); it must match mscorlib's
            // String.GetLegacyNonRandomizedHashCode, which ManagedHash copies.
            string[] samples = { "", "a", "ab", "abc", "Assets/Prefabs/Card.prefab", "x\0y", "中文é￿", new string('z', 4097) };
            int failures = 0;
            foreach (var s in samples)
            {
                int expected = ManagedHash(s), actual = s.GetHashCode();
                if (expected != actual)
                {
                    failures++;
                    Debug.Log($"[BppIos] selftest string hash MISMATCH len={s.Length} expected={expected} actual={actual}");
                }
            }
            Debug.Log($"[BppIos] selftest string hash {(failures == 0 ? "ok" : "FAILED")} ({samples.Length} strings)");
            // The host replaces the App's numeric bundle version with the game's before the engine starts.
            Debug.Log($"[BppIos] selftest application.version={Application.version}");
        }

        static unsafe int ManagedHash(string s)
        {
            fixed (char* first = s)
            {
                int h1 = 5381, h2 = h1;
                char* p = first;
                int c;
                while ((c = *p) != 0)
                {
                    h1 = ((h1 << 5) + h1) ^ c;
                    c = p[1];
                    if (c == 0) break;
                    h2 = ((h2 << 5) + h2) ^ c;
                    p += 2;
                }
                return unchecked(h1 + h2 * 1566083941);
            }
        }
    }
}
