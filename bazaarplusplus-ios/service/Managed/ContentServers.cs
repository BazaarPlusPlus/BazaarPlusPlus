using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.CDN;
using SteamKit2.Internal;

namespace BazaarPlusPlus.Ios.Service
{
    public static class ContentServers
    {
        public static Server[] Select(CContentServerDirectory_GetServersForSteamPipe_Response response, uint app)
        {
            // SteamKit 2.5 maps optional TLS to HTTP. Keep the advertised TLS
            // capability and apply all access restrictions before conversion.
            return response.servers.Where(s => !s.use_as_proxy && !s.steam_china_only &&
                (s.https_support == "mandatory" || s.https_support == "optional") &&
                (s.allowed_app_ids.Count == 0 || s.allowed_app_ids.Contains(app)) &&
                Uri.CheckHostName(s.vhost) == UriHostNameType.Dns)
                .OrderBy(s => s.weighted_load).Take(8)
                .Select(s => (Server)new DnsEndPoint(s.vhost, 443)).ToArray();
        }
        public static async Task<Server[]> LoadWeb(SteamClient client, uint app, CancellationToken cancellation)
        {
            using (var directory = client.Configuration.GetAsyncWebAPIInterface("IContentServerDirectoryService"))
            {
                cancellation.ThrowIfCancellationRequested();
                var response = await directory.CallProtobufAsync<CContentServerDirectory_GetServersForSteamPipe_Response>(
                    HttpMethod.Get, "GetServersForSteamPipe", 1,
                    new Dictionary<string, object?> { ["cell_id"] = "0", ["max_servers"] = "100" });
                cancellation.ThrowIfCancellationRequested();
                var selected = Select(response, app);
                Console.WriteLine($"STEAM_CDN_WEB total={response.servers.Count} eligible={selected.Length}");
                return selected;
            }
        }
        public static async Task<Server[]> Load(SteamClient client, uint app, uint cell)
        {
            var service = client.GetHandler<SteamUnifiedMessages>()!.CreateService<IContentServerDirectory>();
            var request = new CContentServerDirectory_GetServersForSteamPipe_Request { cell_id = cell, max_servers = 100 };
            var message = await service.SendMessage(api => api.GetServersForSteamPipe(request));
            var response = message.GetDeserializedResponse<CContentServerDirectory_GetServersForSteamPipe_Response>();
            var selected = Select(response, app);
            Console.WriteLine($"STEAM_CDN_DIRECTORY total={response.servers.Count} mandatory={response.servers.Count(s => s.https_support == "mandatory")} optional={response.servers.Count(s => s.https_support == "optional")} eligible={selected.Length}");
            return selected;
        }
    }
}
