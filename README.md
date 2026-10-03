# Netcode Sample

A two-player, peer-to-peer, deterministic rollback netcode sample built with Unity.

## Requirements

- Unity **6000.3.11f1** (Unity 6.3)
- Git with [Git LFS](https://git-lfs.com/) (binary assets are stored in LFS)
- [DOTween](#dotween-manual-install) installed locally (not included in this repository)
- [Rust](https://rustup.rs) 1.88 or newer, to build the [Tickwise native library](#tickwise-local-build) and install its CLI

## Dependencies

Unity Package Manager installs these automatically from `Packages/manifest.json`:

| Package | Source | Used for |
|---|---|---|
| [FishNet](https://github.com/FirstGearGames/FishNet) | git, tag `4.7.3` | Networking |
| [Deterministic Path Finding](https://github.com/cosgunhalil/Deterministic-Path-Finding) | git, `main` | Deterministic, fixed-point pathfinding |
| [Connected Rooms Generator](https://github.com/cosgunhalil/Connected-Rooms-Generator) | git, `main` | Procedural room generation |
| [HannibalUI](https://github.com/cosgunhalil/HannibalUI) | git, `master` | User interface |
| [UniTask](https://github.com/Cysharp/UniTask) | git, tag `2.5.11` | Required by HannibalUI |
| [Tickwise](https://github.com/cosgunhalil/Tickwise) | git, pinned commit | Recording and comparing simulation hashes to measure determinism |
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

### Tickwise (local build)

The Tickwise Unity package has no prebuilt binaries yet, so its native library `tickwise_ffi.dll` is built locally and **not versioned** (`Assets/Plugins/Tickwise/` is git-ignored). Without it the project compiles, but recording fails at runtime with `DllNotFoundException`.

With the Unity Editor **closed** (it locks the DLL once loaded), run from the repository root:

```powershell
./tools/build-tickwise.ps1 -InstallCli
```

The script reads the Tickwise commit pinned in `Packages/manifest.json`, builds the library from that commit into `.tickwise-build/`, and copies it to `Assets/Plugins/Tickwise/x86_64/`. `-InstallCli` also installs the `tickwise` command line tool from the same commit. Install it this way rather than from crates.io, whose release can't read the state dumps the Unity package records. Run the script again whenever the pinned commit changes.

## Level

The sample has one level, built in the editor and saved with the scene. To rebuild it, open `Main.unity` and run **Netcode Sample > Build Level**. The command:

1. Generates connected rooms with Connected Rooms Generator, using the settings and fixed seed in `Assets/NetcodeSample/Level/LevelGeneration.asset`.
2. Bakes the Unity NavMesh and converts it to a deterministic DPF navmesh (`Level/DPF-NavMesh.asset`).
3. Places the red base in the start room and the blue base in the room farthest from it, and stores both positions as fixed-point values in `Level/Level.asset`.

Rebuilding replaces the previous level. Both peers must run the same build of the level; its DPF navmesh checksum is logged by the command.

## Measuring determinism with Tickwise

Every hot-seat session is recorded to `<persistentDataPath>/tickwise/hotseat-<time>.rec`: both players' inputs and a state hash for every tick, a full hash every 30 ticks, and a field-by-field state dump every 150 ticks. Open the folder with **Netcode Sample > Tickwise > Open Recordings Folder**.

**Self-check (one machine).** Run **Netcode Sample > Tickwise > Self-Check Recording...** and pick a recording. It replays the recorded inputs through a fresh simulation and checks every tick's hash, then writes the replay as `<name>.replay.rec` and runs `tickwise compare` on the two. A deterministic simulation reports *identical*.

**Catch a planted bug.** The hot-seat readout has a **Chaos** button. It plants a realistic determinism bug from the next tick: newly spawned cubes take their spawn offset from the wall clock instead of the spawn counter, so a replay puts them somewhere else. Play a little with chaos on, stop, and self-check that recording. Tickwise names the tick and the fields:

```text
verdict  first divergence at tick 121, caught by the light hash, confirmed by the full hash at tick 150
tick 150  9 differences over 699 fields
  exact   units[10].position.x: 161008 versus 70805
  exact   units[11].position.x: -1539743 versus -1586102
  ...
```

The self-check runs `tickwise diff --at <first dump tick after the divergence>` for you. By hand:

```bash
tickwise compare a.rec b.rec
```

```bash
tickwise diff a.rec b.rec --at 150
```

Exit codes: 0 identical, 1 different, 2 error. Recordings are only comparable when made with the same game rules and level; each one stores their hashes in its build description.

## Getting started

1. Clone the repository and run `git lfs pull`.
2. Open the project folder with Unity 6000.3.11f1 through Unity Hub. Package Manager resolves the dependencies on first open.
3. Install [DOTween](#dotween-manual-install).
4. Close Unity and build [Tickwise](#tickwise-local-build).
5. Open `Assets/NetcodeSample/Scenes/Main.unity`.
