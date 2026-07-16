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

- `k8s/rbac-build.yaml` — Role/RoleBinding granting the runner's ServiceAccount
  (`system:serviceaccount:github-runner:github-runner`) the `jobs`/`pods`/`pods/log`/
  `pods/exec` access it needs in the `default` namespace to run and clean up the
  build Job. Without this, `Wait For Unity Build Job` fails with a Forbidden error.

## One-time cluster setup

Agones itself isn't part of the per-push pipeline — install it once per cluster:

```bash
helm repo add agones https://agones.dev/chart/stable
helm repo update
helm install agones --namespace agones-system --create-namespace agones/agones
```

Grant the CI runner the permissions it needs to manage the build Job:

```bash
kubectl apply -f k8s/rbac-build.yaml
```

The `unity-ci-build` GHCR package (pushed by the "Build and Push Unity CI Image"
step) needs to be **public**, so the cluster nodes can pull it without a separate
imagePullSecret: GitHub → org → Packages → `unity-ci-build` → Package settings →
Change visibility → Public. (It won't exist until after the first successful push.)

Then apply the fleet manifest (CI does this on every push to `dev`):

```bash
kubectl apply -f k8s/fleet.yaml
```

To manually claim a server for testing:

```bash
kubectl create -f k8s/gameserverallocation.yaml -o yaml
```

## Troubleshooting a stuck build Job

A pod wedged in `ImagePullBackOff` (or anything else that never reaches
Complete/Failed) won't be cleaned up by `ttlSecondsAfterFinished` — that only
fires once the Job actually finishes. Check for and remove strays manually:

```bash
kubectl get jobs -A | grep unity-build
kubectl delete job unity-build-<sha> -n default
```
