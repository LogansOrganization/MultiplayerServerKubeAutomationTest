using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class DedicatedServerBootstrap : MonoBehaviour
{
    [SerializeField] private int maxPlayers = 8;

    private void Start()
    {
#if UNITY_SERVER
        StartServer();
#else
        StartClient();
#endif
    }

    private void StartServer()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("No NetworkManager found in scene!");
            return;
        }

        bool started = NetworkManager.Singleton.StartServer();

        if (started)
        {
            Debug.Log("Dedicated server started successfully");

            // Tells Agones this GameServer is warm and can be handed out by the
            // Fleet, and starts the periodic health ping the sidecar expects.
            AgonesSdk.Instance.Ready();

            // Registers with the relay connector so players can discover this
            // instance through the server browser instead of needing a direct
            // node address/port.
            ushort gamePort = NetworkManager.Singleton.GetComponent<UnityTransport>().ConnectionData.Port;
            string serverName = System.Environment.GetEnvironmentVariable("HOSTNAME") ?? SystemInfo.deviceUniqueIdentifier;
            RelayConnectorClient.Instance.Register(serverName, gamePort, maxPlayers);

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            Application.quitting += AgonesSdk.Instance.Shutdown;
            Application.quitting += RelayConnectorClient.Instance.Deregister;
        }
        else
        {
            Debug.LogError("Failed to start dedicated server");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // First player joining marks this instance Allocated so Agones knows
        // it's hosting a match and won't hand it out again or reap it early.
        AgonesSdk.Instance.Allocate();
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void StartClient()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("No NetworkManager found in scene!");
            return;
        }

        // Agones hands out a different address:port per allocation (dynamic
        // port policy), so the target can't be baked into the scene. Pass it
        // at launch instead: MyClient.exe -ip <address> -port <port>. With no
        // args, show the server browser so the player can pick a live
        // GameServer from the relay's registry instead.
        string ip = GetArg("-ip");
        string portArg = GetArg("-port");

        if (!string.IsNullOrEmpty(ip) && ushort.TryParse(portArg, out ushort port))
        {
            Debug.Log($"Connecting to {ip}:{port} (from command line)");
            ConnectToServer(ip, port);
        }
        else
        {
            ServerBrowserUI.Show(ConnectToServer);
        }
    }

    private void ConnectToServer(string ip, ushort port)
    {
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, port);

        NetworkManager.Singleton.OnClientConnectedCallback += HandleLocalClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleLocalClientDisconnected;

        bool started = NetworkManager.Singleton.StartClient();
        Debug.Log(started ? $"Client started, attempting to connect to {ip}:{port}..." : "Failed to start client");
    }

    private static string GetArg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private void HandleLocalClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("Successfully connected to server!");
        }
    }

    private void HandleLocalClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.LogError("Disconnected from server (connection failed or lost).");
        }
    }
}