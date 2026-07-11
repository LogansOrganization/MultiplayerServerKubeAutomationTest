using Unity.Netcode;
using UnityEngine;

public class DedicatedServerBootstrap : MonoBehaviour
{
    private void Start()
    {
#if UNITY_SERVER
        StartServer();
#else
        Debug.Log("Client build started");
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
        }
        else
        {
            Debug.LogError("Failed to start dedicated server");
        }
    }
}