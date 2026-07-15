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
- `Dockerfile` — packages the build output onto `ubuntu:22.04`.
- `Dockerfile.unity-build` / `entrypoint.sh` — Unity Editor image used to actually run
  the headless build (clone → activate license → `Build.Server` → return license).
- `k8s/build-job.yaml` — templated Kubernetes `Job` that runs the above image as a
  native pod. The CI runner is itself a containerized (actions-runner-controller) pod
  that reaches Docker through a passthrough socket, which breaks Docker-in-Docker
  bind-mounts (e.g. `game-ci/unity-builder`'s own entrypoint) — running the actual
  Unity build as its own scheduled Job sidesteps that entirely. CI applies it, waits
  for it, `kubectl cp`s `buildServer/` out of the pod, then deletes the Job.
- `k8s/fleet.yaml` — Agones `Fleet`: the pool of warm game server instances.
- `k8s/gameserverallocation.yaml` — reference manifest for manually claiming a
  `Ready` instance (a real matchmaker would call the Agones Allocator service instead).
- `.github/workflows/deploy.yml` — builds the Unity server via the Job above,
  builds/pushes the runtime image, bumps the tag in `k8s/fleet.yaml`, and applies it.

The runner's cluster identity needs `create`/`get`/`delete` on `jobs` and
`get`/`list` on `pods` plus `pods/exec` (required by `kubectl cp` and `kubectl logs`)
in whatever namespace the build Job runs in — that RBAC isn't part of this repo.

## One-time cluster setup

Agones itself isn't part of the per-push pipeline — install it once per cluster:

```bash
helm repo add agones https://agones.dev/chart/stable
helm repo update
helm install agones --namespace agones-system --create-namespace agones/agones
```

Then apply the fleet manifest (CI does this on every push to `dev`):

```bash
kubectl apply -f k8s/fleet.yaml
```

To manually claim a server for testing:

```bash
kubectl create -f k8s/gameserverallocation.yaml -o yaml
```
