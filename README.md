# MultiplayerServerKubeAutomationTest

Unity dedicated server (Netcode for GameObjects) running on Kubernetes via
[Agones](https://agones.dev/) instead of a plain Deployment/Service — Agones adds
session-aware lifecycle (won't kill a pod mid-match), dynamic UDP port allocation,
in-process health/readiness signaling, and fleet scaling based on player demand.

## Pieces

- `Assets/Editor/Build.cs` — headless Linux server build (`Build.Server`), output to `buildServer/Server`.
- `Assets/scripts/DedicatedServerBootstrap.cs` — starts the Netcode server; reports
  `Ready()` once listening, `Allocate()` on first client connect, `Shutdown()` on exit.
- `Assets/scripts/AgonesSdk.cs` — talks to the Agones SDK sidecar over its local REST
  gateway (`localhost:9358`), including the periodic health ping.
- `Dockerfile` — packages the build output onto `ubuntu:22.04`. Project-specific,
  rebuilt every push.
- `k8s/fleet.yaml` — Agones `Fleet`: the pool of warm game server instances.
  Project-specific (replicas/resources/image name).
- `k8s/gameserverallocation.yaml` — reference manifest for manually claiming a
  `Ready` instance (a real matchmaker would call the Agones Allocator service instead).
- `.github/workflows/deploy.yml` — a thin caller into the shared reusable workflow at
  [LogansOrganization/unity-k8s-pipeline](https://github.com/LogansOrganization/unity-k8s-pipeline),
  which does the actual work: spins up a throwaway Kubernetes Job to run the Unity
  build (native pod, not Docker-in-Docker — the runner is a containerized ARC pod
  that can't reliably bind-mount through its passthrough Docker socket), extracts the
  output, builds/pushes the runtime image, applies `k8s/fleet.yaml`, and waits for
  rollout. See that repo's README for how the Job/license/secrets machinery works,
  and for adding a new game project to the same pipeline.

## One-time cluster setup

Agones install, the CI runner's RBAC, and the GHCR pull secret are one-time
*per cluster*, not per project — see
[unity-k8s-pipeline's README](https://github.com/LogansOrganization/unity-k8s-pipeline#one-time-cluster-setup)
for those (already done for this cluster).

Apply the fleet manifest (CI does this on every push to `dev`):

```bash
kubectl apply -f k8s/fleet.yaml
```

To find a server to test against, list the `GameServer` resources directly —
read-only, doesn't claim/mutate anything:

```bash
kubectl get gameservers
```

The `ADDRESS`/`PORT` columns are the node's externally-reachable IP and the
hostPort Agones mapped to `containerPort: 7777` (this differs per instance
under a `Dynamic` port policy, so it can't be baked into the scene — see
`DedicatedServerBootstrap.StartClient()`, which reads it from the command
line instead). `kubectl logs <gameserver-name> -c unity-server` shows the
Unity process's own output (note `-c`: the pod has two containers, the game
server and the Agones sidecar).

Point a client build at a `Ready` server directly — our own code calls
`Allocate()` on first connect, so no separate allocation step is needed for
testing:

```bash
YourClientBuild.exe -ip <ADDRESS> -port <PORT>
```

For same-machine Editor testing, skip the args and just set `Address`/`Port`
on the `NetworkManager`'s `UnityTransport` component in the Inspector instead.

`k8s/gameserverallocation.yaml` is what a real matchmaker uses instead — it
claims a specific `Ready` instance exclusively (Agones won't hand the same one
to a second request) rather than just reading state:

```bash
kubectl create -f k8s/gameserverallocation.yaml -o yaml
```

## Troubleshooting a stuck build Job

See [unity-k8s-pipeline's README](https://github.com/LogansOrganization/unity-k8s-pipeline#troubleshooting-a-stuck-build-job).
