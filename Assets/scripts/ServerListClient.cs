using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// Talks to the relay's player-facing HTTP API (see
// UnityK8Relay/src/bin/relay/http_api.rs, GET /servers). Plain UnityWebRequest
// GET, same one-shot-call-per-request coroutine style as AgonesSdk.cs, since
// this really is HTTP — unlike the dedicated server's raw-TCP control channel
// (RelayConnectorClient.cs).
public class ServerListClient : MonoBehaviour
{
    [Serializable]
    public class ServerListing
    {
        public string name;
        public int player_count;
        public int max_players;
        public string connect_address;
    }

    // JsonUtility can't parse a bare top-level JSON array, so the response
    // body gets wrapped in {"items": ...} before parsing.
    [Serializable]
    private class ServerListingArrayWrapper
    {
        public List<ServerListing> items;
    }

    public IEnumerator FetchServers(string relayHttpBaseUrl, Action<List<ServerListing>> onSuccess, Action<string> onError)
    {
        using UnityWebRequest request = UnityWebRequest.Get($"{relayHttpBaseUrl}/servers");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(request.error);
            yield break;
        }

        ServerListingArrayWrapper wrapper;
        try
        {
            wrapper = JsonUtility.FromJson<ServerListingArrayWrapper>("{\"items\":" + request.downloadHandler.text + "}");
        }
        catch (Exception e)
        {
            onError?.Invoke($"malformed response: {e.Message}");
            yield break;
        }

        onSuccess?.Invoke(wrapper.items ?? new List<ServerListing>());
    }
}
