using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Speaks the relay connector's control-channel protocol over a raw TcpClient —
// UnityWebRequest is HTTP-only, so the AgonesSdk.cs REST pattern doesn't apply
// here. Wire format matches UnityK8Relay/src/framing.rs + protocol.rs exactly:
// each frame is [1-byte kind][4-byte big-endian length][UTF8 JSON payload].
// kind 0x01 (control/JSON) is the only one this client ever sends or expects —
// the data-tunnel and session-close kinds are relay<->connector internals this
// client never sees.
public class RelayConnectorClient : MonoBehaviour
{
    private const byte KindControl = 0x01;
    private const int HeaderLength = 5;
    private const int HeartbeatIntervalSeconds = 10;
    // Connectors that ack heartbeats (UnityK8Relay since Oct 2026): once one ack has arrived,
    // this many heartbeats without one means the connector is gone. A deleted connector pod
    // sends no TCP reset, so without this the writes would just queue for ~15 minutes.
    private const int MissedAcksBeforeReconnect = 3;
    private const int RegisterTimeoutSeconds = 10;

    private static RelayConnectorClient instance;

    public static RelayConnectorClient Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject(nameof(RelayConnectorClient));
                instance = go.AddComponent<RelayConnectorClient>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private TcpClient client;
    private NetworkStream stream;
    private CancellationTokenSource cts;

    public void Register(string serverName, ushort gamePort, int maxPlayers)
    {
        cts = new CancellationTokenSource();
        _ = RunAsync(serverName, gamePort, maxPlayers, cts.Token);
    }

    // Best-effort graceful notice to the connector. Hooked to Application.quitting,
    // so this has to run synchronously to completion rather than leave a Task
    // dangling past process exit.
    public void Deregister()
    {
        CancellationTokenSource localCts = cts;
        cts = null;
        localCts?.Cancel();
        localCts?.Dispose();

        NetworkStream localStream = stream;
        TcpClient localClient = client;
        stream = null;
        client = null;

        if (localStream == null || localClient == null || !localClient.Connected)
        {
            localClient?.Close();
            return;
        }

        try
        {
            localStream.WriteTimeout = 2000;
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new TypeOnlyMessage { type = "Deregister" }));
            WriteFrameSync(localStream, payload);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Relay connector deregister failed: {e.Message}");
        }
        finally
        {
            localClient.Close();
        }
    }

    // Keeps this server registered for as long as it runs: if the connection drops (connector
    // restart, node reboot, network blip) it reconnects and registers again, backing off 2s..30s.
    private async Task RunAsync(string serverName, ushort gamePort, int maxPlayers, CancellationToken token)
    {
        string host = GetEnv("CONNECTOR_HOST", "connector.unity-relay.svc.cluster.local");
        int port = int.Parse(GetEnv("CONNECTOR_PORT", "7000"));
        string podIp = GetEnv("POD_IP", "127.0.0.1");
        string secret = Environment.GetEnvironmentVariable("GAMESERVER_SHARED_SECRET");

        if (string.IsNullOrEmpty(secret))
        {
            Debug.LogError("GAMESERVER_SHARED_SECRET is not set; cannot register with the relay connector.");
            return;
        }

        int backoffSeconds = 2;
        while (!token.IsCancellationRequested)
        {
            bool registered = await ConnectAndServeAsync(host, port, podIp, secret, serverName, gamePort, maxPlayers, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            // A session that got registered resets the backoff; repeated failures slow down.
            backoffSeconds = registered ? 2 : Math.Min(backoffSeconds * 2, 30);
            Debug.Log($"Reconnecting to the relay connector in {backoffSeconds}s...");
            try
            {
                await Task.Delay(backoffSeconds * 1000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // One connection: register, then heartbeat until it fails. Returns whether it got registered.
    private async Task<bool> ConnectAndServeAsync(string host, int port, string podIp, string secret,
        string serverName, ushort gamePort, int maxPlayers, CancellationToken token)
    {
        bool registered = false;
        try
        {
            client?.Close();
            client = new TcpClient();
            await client.ConnectAsync(host, port).ConfigureAwait(false);
            stream = client.GetStream();

            RegisterMessage register = new RegisterMessage
            {
                type = "Register",
                server_name = serverName,
                secret = secret,
                max_players = maxPlayers,
                pod_addr = $"{podIp}:{gamePort}"
            };
            await WriteFrameAsync(stream, Encoding.UTF8.GetBytes(JsonUtility.ToJson(register)), token).ConfigureAwait(false);

            Task<byte[]> ackRead = ReadFramePayloadAsync(stream, token);
            if (await Task.WhenAny(ackRead, Task.Delay(RegisterTimeoutSeconds * 1000, token)).ConfigureAwait(false) != ackRead)
            {
                throw new TimeoutException($"Relay connector didn't answer Register within {RegisterTimeoutSeconds}s.");
            }
            byte[] ackPayload = await ackRead.ConfigureAwait(false);
            RelayResponse ack = JsonUtility.FromJson<RelayResponse>(Encoding.UTF8.GetString(ackPayload));

            // Retried by the caller: right after a reconnect the relay can still hold the old
            // registration under this name until its heartbeat times out.
            if (ack.type == "RegisterNack")
            {
                Debug.LogWarning($"Relay connector rejected registration: {ack.reason}");
                return false;
            }

            if (ack.type != "RegisterAck")
            {
                Debug.LogWarning($"Relay connector sent an unexpected response to Register: {ack.type}");
                return false;
            }

            registered = true;
            Debug.Log($"Registered with relay connector as '{serverName}' ({podIp}:{gamePort}).");

            // Reads everything the connector sends from here on: heartbeat acks, and the end of
            // the connection, which this notices straight away instead of at the next write.
            AckClock acks = new AckClock();
            NetworkStream readStream = stream;
            Task reader = Task.Run(() => ReadAcksAsync(readStream, acks, token));
            TimeSpan ackTimeout = TimeSpan.FromSeconds(HeartbeatIntervalSeconds * MissedAcksBeforeReconnect);

            while (!token.IsCancellationRequested)
            {
                Task delay = Task.Delay(HeartbeatIntervalSeconds * 1000, token);
                if (await Task.WhenAny(delay, reader).ConfigureAwait(false) == reader)
                {
                    await reader.ConfigureAwait(false);   // rethrows why the read side ended
                    throw new IOException("Relay connector closed the connection.");
                }
                await delay.ConfigureAwait(false);

                if (acks.SilentFor(ackTimeout))
                {
                    throw new TimeoutException($"Relay connector stopped answering heartbeats for {ackTimeout.TotalSeconds:0}s.");
                }

                byte[] heartbeat = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new TypeOnlyMessage { type = "Heartbeat" }));
                await WriteFrameAsync(stream, heartbeat, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Deregister() already handled connection teardown.
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Relay connector connection error: {e.Message}");
        }
        finally
        {
            // A dead connection is dropped here so its pending read ends too; on cancel,
            // Deregister() owns the teardown.
            if (!token.IsCancellationRequested)
            {
                client?.Close();
            }
        }

        return registered;
    }

    private static async Task ReadAcksAsync(NetworkStream source, AckClock acks, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            byte[] payload = await ReadFramePayloadAsync(source, token).ConfigureAwait(false);
            RelayResponse message = JsonUtility.FromJson<RelayResponse>(Encoding.UTF8.GetString(payload));
            if (message != null && message.type == "HeartbeatAck")
            {
                acks.Seen();
            }
        }
    }

    // When the connector last acked a heartbeat. Before the first ack it never counts as silent,
    // so this still works against connectors that don't ack.
    private sealed class AckClock
    {
        private readonly object gate = new object();
        private readonly System.Diagnostics.Stopwatch sinceLastAck = new System.Diagnostics.Stopwatch();
        private bool anySeen;

        public void Seen()
        {
            lock (gate)
            {
                anySeen = true;
                sinceLastAck.Restart();
            }
        }

        public bool SilentFor(TimeSpan limit)
        {
            lock (gate)
            {
                return anySeen && sinceLastAck.Elapsed > limit;
            }
        }
    }

    private static string GetEnv(string name, string fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static async Task WriteFrameAsync(NetworkStream target, byte[] payload, CancellationToken token)
    {
        byte[] header = BuildHeader(payload.Length);
        await target.WriteAsync(header, 0, header.Length, token).ConfigureAwait(false);
        await target.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
    }

    private static void WriteFrameSync(NetworkStream target, byte[] payload)
    {
        byte[] header = BuildHeader(payload.Length);
        target.Write(header, 0, header.Length);
        target.Write(payload, 0, payload.Length);
    }

    private static byte[] BuildHeader(int payloadLength)
    {
        uint len = (uint)payloadLength;
        return new byte[]
        {
            KindControl,
            (byte)(len >> 24),
            (byte)(len >> 16),
            (byte)(len >> 8),
            (byte)len
        };
    }

    private static async Task<byte[]> ReadFramePayloadAsync(NetworkStream source, CancellationToken token)
    {
        byte[] header = await ReadExactAsync(source, HeaderLength, token).ConfigureAwait(false);
        uint length = ((uint)header[1] << 24) | ((uint)header[2] << 16) | ((uint)header[3] << 8) | header[4];
        return await ReadExactAsync(source, (int)length, token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream source, int count, CancellationToken token)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await source.ReadAsync(buffer, offset, count - offset, token).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Relay connector closed the connection.");
            }

            offset += read;
        }

        return buffer;
    }

    [Serializable]
    private class RegisterMessage
    {
        public string type;
        public string server_name;
        public string secret;
        public int max_players;
        public string pod_addr;
    }

    [Serializable]
    private class TypeOnlyMessage
    {
        public string type;
    }

    [Serializable]
    private class RelayResponse
    {
        public string type;
        public string reason;
    }
}
