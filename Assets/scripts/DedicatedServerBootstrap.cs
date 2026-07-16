using Unity.Netcode;
using UnityEngine;

public class DedicatedServerBootstrap : MonoBehaviour
{
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

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            Application.quitting += AgonesSdk.Instance.Shutdown;
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

        NetworkManager.Singleton.OnClientConnectedCallback += HandleLocalClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleLocalClientDisconnected;

        bool started = NetworkManager.Singleton.StartClient();
        Debug.Log(started ? "Client started, attempting to connect..." : "Failed to start client");
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