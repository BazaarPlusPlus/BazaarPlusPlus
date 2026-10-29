using System;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus.Ios.Helper
{
    // Makes one HTTPS request through the managed HttpClient stack (Mono TLS, not UnityWebRequest) and logs
    // the full exception chain, which games usually reduce to "The SSL connection could not be established".
    public static class TlsProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            var url = Environment.GetEnvironmentVariable("BPP_TLS_PROBE_URL");
            if (string.IsNullOrEmpty(url)) return;
            Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient();
                    var response = await client.GetAsync(url);
                    Debug.Log($"[BppIos] tls probe {url}: HTTP {(int)response.StatusCode}");
                }
                catch (Exception e)
                {
                    Debug.Log($"[BppIos] tls probe {url} failed: {e}");
                }
            });
        }
    }
}
