# Netcode Sample

A two-player, peer-to-peer, deterministic rollback netcode sample built with Unity.

## Requirements

- Unity **6000.3.11f1** (Unity 6.3)
- Git with [Git LFS](https://git-lfs.com/) (binary assets are stored in LFS)
- [DOTween](#dotween-manual-install) installed locally (not included in this repository)

## Dependencies

Unity Package Manager installs these automatically from `Packages/manifest.json`:

| Package | Source | Used for |
|---|---|---|
| [FishNet](https://github.com/FirstGearGames/FishNet) | git, tag `4.7.3` | Networking |
| [Deterministic Path Finding](https://github.com/cosgunhalil/Deterministic-Path-Finding) | git, `main` | Deterministic, fixed-point pathfinding |
| [Connected Rooms Generator](https://github.com/cosgunhalil/Connected-Rooms-Generator) | git, `main` | Procedural room generation |
| [HannibalUI](https://github.com/cosgunhalil/HannibalUI) | git, `master` | User interface |
| [UniTask](https://github.com/Cysharp/UniTask) | git, tag `2.5.11` | Required by HannibalUI |
| [ParrelSync](https://github.com/VeriorPies/ParrelSync) | git, tag `1.5.3` | Running a second editor (clone) to test two peers locally |
| ProBuilder `6.1.2` | Unity registry | Required by Connected Rooms Generator |
| AI Navigation | Unity registry | NavMesh baking for room generation and pathfinding |

`Packages/packages-lock.json` pins the exact commit of every git package.

### DOTween (manual install)

HannibalUI depends on [DOTween](https://github.com/Demigiant/dotween), which is not distributed as a Unity package. It is **not versioned in this repository** (`Assets/Plugins/Demigiant/` is git-ignored), so every clone has to install it once. Until it is installed, HannibalUI fails to compile.

1. Get DOTween (free) from either source:
   - [Unity Asset Store](https://assetstore.unity.com/packages/tools/animation/dotween-hotween-v2-27676): import it into the project, or
   - the [DOTween repository](https://github.com/Demigiant/dotween): copy `UnityTests.Unity6000.3/Assets/Plugins/Demigiant/DOTween/` to `Assets/Plugins/Demigiant/DOTween/`.
2. In Unity, open **Tools > Demigiant > DOTween Utility Panel** and click **Setup DOTween...**.
3. Enable **Create ASMDEF** and apply. This creates the `DOTween.Modules` assembly definition that HannibalUI references.
4. Wait for Unity to recompile; the console should have no errors.

## Getting started

1. Clone the repository and run `git lfs pull`.
2. Open the project folder with Unity 6000.3.11f1 through Unity Hub. Package Manager resolves the dependencies on first open.
3. Install [DOTween](#dotween-manual-install).
4. Open `Assets/Scenes/SampleScene.unity`.
