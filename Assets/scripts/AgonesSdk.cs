using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Talks to the Agones SDK sidecar over its local REST gateway (default port 9358)
// instead of the gRPC SDK, since grpc-dotnet doesn't play well with Unity/IL2CPP
// Linux server builds. Calls are fire-and-forget: if the sidecar isn't present
// (e.g. running outside a GameServer pod) this just logs a warning and moves on.
public class AgonesSdk : MonoBehaviour
{
    private const float HealthIntervalSeconds = 5f;

    private static AgonesSdk instance;

    public static AgonesSdk Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject(nameof(AgonesSdk));
                instance = go.AddComponent<AgonesSdk>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private string baseUrl;

    private void Awake()
    {
        string port = System.Environment.GetEnvironmentVariable("AGONES_SDK_HTTP_PORT");
        baseUrl = $"http://localhost:{(string.IsNullOrEmpty(port) ? "9358" : port)}";
    }

    public void Ready()
    {
        StartCoroutine(Post("/ready"));
        StartCoroutine(HealthLoop());
    }

    public void Allocate()
    {
        StartCoroutine(Post("/allocate"));
    }

    public void Shutdown()
    {
        StartCoroutine(Post("/shutdown"));
    }

    private IEnumerator HealthLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(HealthIntervalSeconds);
        while (true)
        {
            yield return Post("/health");
            yield return wait;
        }
    }

    private IEnumerator Post(string path)
    {
        using UnityWebRequest request = new UnityWebRequest($"{baseUrl}{path}", "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"Agones SDK call to {path} failed: {request.error}");
        }
    }
}
