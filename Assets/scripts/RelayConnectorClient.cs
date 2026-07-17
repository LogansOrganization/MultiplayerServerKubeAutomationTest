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

        try
        {
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

            byte[] ackPayload = await ReadFramePayloadAsync(stream, token).ConfigureAwait(false);
            RelayResponse ack = JsonUtility.FromJson<RelayResponse>(Encoding.UTF8.GetString(ackPayload));

            if (ack.type == "RegisterNack")
            {
                Debug.LogError($"Relay connector rejected registration: {ack.reason}");
                return;
            }

            if (ack.type != "RegisterAck")
            {
                Debug.LogError($"Relay connector sent an unexpected response to Register: {ack.type}");
                return;
            }

            Debug.Log($"Registered with relay connector as '{serverName}' ({podIp}:{gamePort}).");

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatIntervalSeconds * 1000, token).ConfigureAwait(false);
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
