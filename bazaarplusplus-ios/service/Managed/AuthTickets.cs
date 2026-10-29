// Protocol implementation adapted from SteamRE/SteamKit's SteamAuthTicket (LGPL-2.1-only).
// See THIRD_PARTY.md. Tickets remain in memory and are cancelled with their owning session.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;

namespace BazaarPlusPlus.Ios.Service
{
    public sealed class AuthTickets : ClientMsgHandler
    {
        readonly ConcurrentQueue<byte[]> tokens = new ConcurrentQueue<byte[]>();
        readonly ConcurrentDictionary<uint, CMsgAuthTicket> active = new ConcurrentDictionary<uint, CMsgAuthTicket>();
        readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> acknowledgements = new ConcurrentDictionary<uint, TaskCompletionSource<bool>>();
        int sequence;
        public static uint Crc(byte[] bytes)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int i = 0; i < 8; ++i) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            return ~crc;
        }
        public async Task<byte[]> Create(uint app, uint handle, string identity, CancellationToken cancellation)
        {
            var ownership = await Client.GetHandler<SteamApps>()!.GetAppOwnershipTicket(app);
            cancellation.ThrowIfCancellationRequested();
            if (ownership.Result != EResult.OK) throw new InvalidOperationException("App ownership denied: " + ownership.Result);
            byte[] token;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!tokens.TryDequeue(out token!))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("No game-connect token received");
                await Task.Delay(100, cancellation);
            }
            byte[] auth;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(token.Length); writer.Write(token);
                    writer.Write(24); writer.Write(1); writer.Write(5u); // Web API ticket type
                    byte[] random = new byte[8]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
                    writer.Write(random); writer.Write(unchecked((uint)Environment.TickCount));
                    writer.Write(Interlocked.Increment(ref sequence));
                }
                auth = stream.ToArray();
            }
            uint crc = Crc(auth);
            var ack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            acknowledgements[crc] = ack;
            active[handle] = new CMsgAuthTicket { gameid = app, ticket = auth, ticket_crc = crc,
                server_secret = string.IsNullOrEmpty(identity) ? null! : Encoding.UTF8.GetBytes("str:" + identity + "\0") };
            SendTickets();
            try
            {
                if (await Task.WhenAny(ack.Task, Task.Delay(15000, cancellation)) != ack.Task)
                { cancellation.ThrowIfCancellationRequested(); throw new TimeoutException("Steam did not acknowledge the Web API ticket"); }
                await ack.Task;
                cancellation.ThrowIfCancellationRequested();
                int length = auth.Length + 4 + ownership.Ticket.Length;
                if (length > 2560) throw new InvalidDataException("Web API ticket too large");
                var result = new byte[2560];
                Buffer.BlockCopy(auth, 0, result, 0, auth.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(ownership.Ticket.Length), 0, result, auth.Length, 4);
                Buffer.BlockCopy(ownership.Ticket, 0, result, auth.Length + 4, ownership.Ticket.Length);
                return result;
            }
            catch { Cancel(handle); throw; }
            finally { acknowledgements.TryRemove(crc, out _); }
        }
        public void Cancel(uint handle) { if (active.TryRemove(handle, out _)) SendTickets(); }
        void SendTickets()
        {
            var message = new ClientMsgProtobuf<CMsgClientAuthList>(EMsg.ClientAuthList);
            message.SourceJobID = Client.GetNextJobID();
            message.Body.tokens_left = (uint)tokens.Count;
            message.Body.tickets.AddRange(active.Values);
            message.Body.app_ids.AddRange(active.Values.Select(t => (uint)t.gameid).Distinct());
            Client.Send(message);
        }
        public override void HandleMsg(IPacketMsg packet)
        {
            if (packet.MsgType == EMsg.ClientGameConnectTokens)
            {
                var body = new ClientMsgProtobuf<CMsgClientGameConnectTokens>(packet).Body;
                foreach (var token in body.tokens) tokens.Enqueue(token);
                while (tokens.Count > body.max_tokens_to_keep) tokens.TryDequeue(out _);
            }
            else if (packet.MsgType == EMsg.ClientAuthListAck)
            {
                foreach (uint crc in new ClientMsgProtobuf<CMsgClientAuthListAck>(packet).Body.ticket_crc)
                    if (acknowledgements.TryGetValue(crc, out var ack)) ack.TrySetResult(true);
            }
        }
    }
}
