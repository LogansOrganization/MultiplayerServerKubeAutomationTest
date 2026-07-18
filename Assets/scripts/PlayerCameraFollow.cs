using Unity.Netcode;
using UnityEngine;

// Lives on the scene's static Main Camera rather than on the player prefab,
// so it doesn't need to fight NGO over object ownership. Polls for the local
// player object each frame since it spawns well after this camera exists
// (title screen -> browser -> connect -> NGO spawns the prefab).
public class PlayerCameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 5f, -8f);
    [SerializeField] private float followSmoothing = 10f;

    private void LateUpdate()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer == null) return;

        Vector3 desiredPosition = localPlayer.transform.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSmoothing * Time.deltaTime);
        transform.LookAt(localPlayer.transform.position + Vector3.up);
    }
}
