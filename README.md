# Netcode Sample

A two-player, peer-to-peer, deterministic rollback netcode sample built with Unity 6.

Red and blue each have a base at opposite ends of a generated level. Small cubes spawn at every base once a second and march to the enemy base, fighting enemy cubes they meet; each player can also spawn a big cube on a cooldown. A cube that reaches the enemy base explodes and damages it by its remaining health. A destroyed base gives the other player a point and starts the next round; after two minutes the healthier base (then the bigger army) wins the round.

## What it demonstrates

- **A deterministic simulation.** Fixed-point math ([Deterministic Path Finding](https://github.com/cosgunhalil/Deterministic-Path-Finding) navmesh, flow fields and ORCA avoidance), no Unity types in the game rules, and snapshot/restore of the whole state.
- **Rollback netcode.** Input delay, prediction, rollback and re-simulation, stalling, time sync, and redundant unreliable inputs, in an engine-free layer with a two-method transport interface.
- **Peer-to-peer over FishNet.** FishNet only carries inputs between two peers; each peer simulates the whole game.
- **Measuring determinism with [Tickwise](https://github.com/cosgunhalil/Tickwise).** Both peers record confirmed ticks; `tickwise compare` names the first divergent tick and `tickwise diff` the diverging fields. A planted bug ("chaos") shows it catching a real desync.
- **Hiding rollback on screen.** Interpolation between ticks, smoothed corrections, and fades for cubes that only existed in a wrong prediction.

## Getting started

### Requirements

- Windows, Unity **6000.3.11f1** (Unity 6.3)
- Git with [Git LFS](https://git-lfs.com/) (binary assets are stored in LFS)
- [DOTween](#dotween-manual-install), installed locally (not included in this repository)
- [Rust](https://rustup.rs) 1.88 or newer, to build the [Tickwise native library](#tickwise-local-build) and its CLI

### Steps

1. Clone the repository and run `git lfs pull`.
2. Open the project with Unity 6000.3.11f1 through Unity Hub. Package Manager resolves the [dependencies](#dependencies) on first open.
3. Install [DOTween](#dotween-manual-install).
4. Close Unity and build [Tickwise](#tickwise-local-build).
5. Open `Assets/NetcodeSample/Scenes/Main.unity`.

### DOTween (manual install)

HannibalUI depends on [DOTween](https://github.com/Demigiant/dotween), which isn't distributed as a Unity package. It's **not versioned in this repository** (`Assets/Plugins/Demigiant/` is git-ignored), so every clone installs it once. Until then, HannibalUI fails to compile.

1. Get DOTween (free) from the [Unity Asset Store](https://assetstore.unity.com/packages/tools/animation/dotween-hotween-v2-27676) and import it, or copy `UnityTests.Unity6000.3/Assets/Plugins/Demigiant/DOTween/` from the [DOTween repository](https://github.com/Demigiant/dotween) to `Assets/Plugins/Demigiant/DOTween/`.
2. In Unity, open **Tools > Demigiant > DOTween Utility Panel** and click **Setup DOTween...**.
3. Enable **Create ASMDEF** and apply. This creates the `DOTween.Modules` assembly definition HannibalUI references.

### Tickwise (local build)

The Tickwise Unity package has no prebuilt binaries yet, so its native library `tickwise_ffi.dll` is built locally and **not versioned** (`Assets/Plugins/Tickwise/` is git-ignored). Without it the project compiles, but recording logs an error and stays off.

With the Unity Editor **closed** (it locks the DLL once loaded), run from the repository root:

```powershell
./tools/build-tickwise.ps1 -InstallCli
```

The script reads the Tickwise commit pinned in `Packages/manifest.json`, builds the library from it into `.tickwise-build/`, and copies it to `Assets/Plugins/Tickwise/x86_64/`. `-InstallCli` also installs the `tickwise` command line tool from the same commit (crates.io's release can't read the state dumps the Unity package records). Run it again whenever the pinned commit changes.

## Playing

The scene runs one mode at a time: a **network match** (the sample itself) or a **local match** (development tools). The two setup commands switch between them.

### Network match (FishNet, peer to peer)

Run **Netcode Sample > Set Up Network Match Scene** once. It adds a FishNet *Network Manager* (Tugboat UDP transport), the *Network Match* object and the *Match UI*.

**Two editors on one machine (ParrelSync)** is the quickest way to play:

1. Open **ParrelSync > Clones Manager**, create a clone and open it. Never edit files in the clone; it shares this project's assets.
2. Press Play in both editors. The original hosts as red, the clone joins localhost as blue; no clicking needed (turn off *Auto Start With ParrelSync* on *Network Match* to use the menu instead).
3. After code changes, click into **both** editors so both recompile.

**Two machines:** press Play, then in the main menu **Host** on one (choose the port and the team the host plays) and **Join** with the host's address on the other. The default port is 7770; Windows may ask to allow it through the firewall.

Before the match starts, the peers compare their game build (rules hash and navmesh checksum, plus a protocol version) and refuse to play with different ones. In the match:

| Control | Does |
|---|---|
| Space, or the HUD's big cube button | Spawn your big cube (5 s cooldown) |
| F1 | Show the netcode overlay: ticks, rollbacks, stalls, time sync, round trip; FishNet's latency simulator; the chaos toggle |
| Leave | Back to the main menu |

The HUD shows both bases' health, the score and your cooldown; a popup shows each round's result until the next round starts.

**Simulated network conditions.** The F1 overlay's latency simulator (latency, packet loss, out-of-order) delays that peer's *outgoing* traffic. Turn it on in both peers for a two-way delay.

### Local match (development tools)

Run **Netcode Sample > Set Up Local Match Scene**, pick a **Mode** on the *Local Match* object, and press Play. Space spawns a red big cube, Enter a blue one.

| Mode | What it runs |
|---|---|
| HotSeat | One simulation, both players on one keyboard. No rollback. |
| SyncTest | Hot-seat, but every tick rolls back and re-simulates the last ticks (*Sync Test Distance*) and reports any tick that comes out different: catches state missing from snapshots. |
| Loopback | Two rollback peers in one process over a simulated network. Latency, jitter and loss have sliders on screen; the readout shows each peer's rollbacks, stalls and time-sync skips. The red peer is drawn. |

## Measuring determinism with Tickwise

Every match is recorded to `<persistentDataPath>/tickwise/` (**Netcode Sample > Tickwise > Open Recordings Folder**): both players' inputs and a state hash for every tick, a full hash (game and navigation state) every 30 ticks, and a field-by-field state dump every 150 ticks.

| Mode | Files | What's recorded |
|---|---|---|
| Network match | `online-<session>-red.rec`, `-blue.rec` (one per peer; with ParrelSync both in the same folder) | Confirmed ticks |
| Loopback | `loopback-<time>-red.rec`, `-blue.rec` | Confirmed ticks |
| HotSeat, SyncTest | `hotseat-<time>.rec`, `synctest-<time>.rec` | Every tick |

Under rollback a peer records a tick only once it's **confirmed** (both players' real inputs known, never re-simulated again), so two peers' recordings compare tick for tick.

### Walkthrough: catch a desync between two peers

1. Start a network match with ParrelSync (or Loopback mode).
2. Play for a few seconds, then press **F1** in the *host* and click **Plant chaos on this peer**. From the next tick, that peer's new cubes take their spawn offset from the wall clock instead of the spawn counter: a realistic determinism bug, on one machine only.
3. Play a few more seconds and stop both editors.
4. Run **Netcode Sample > Tickwise > Compare Two Recordings...** and pick the session's red and blue files. The Console shows `tickwise compare`:

   ```text
   verdict  first divergence at tick 301, caught by the light hash, confirmed by the full hash at tick 330, last agreement at tick 300
   next     tickwise diff online-...-red.rec online-...-blue.rec --at 450
   ```

5. Run the suggested diff (from a terminal in the recordings folder) to see which fields diverged:

   ```bash
   tickwise diff online-<session>-red.rec online-<session>-blue.rec --at 450
   ```

   ```text
   tick 450  41 differences over 979 fields: 0 structural, 41 exact, 0 sub-epsilon float drift
     exact   units[22].position.x: 652157 versus 670993
     exact   units[22].position.z: 602776 versus 668612
     ...
   ```

   The first diverging units are the cubes spawned right after the bug was planted, which points straight at the spawn code.

Without chaos, the same comparison reports `identical over N compared ticks`, also under heavy simulated packet loss.

### Self-check (one recording)

**Netcode Sample > Tickwise > Self-Check Recording...** replays a recording's inputs through a fresh simulation, checks every tick's hash, writes the replay as `<name>.replay.rec` and runs `tickwise compare` (and `diff` at the first dump after a divergence) on the pair. A recording made with chaos on fails at the first chaotic spawn; a clean one reports *identical*. The game rules and level must be the ones the recording was made with.

### CLI

```bash
tickwise inspect a.rec
```

```bash
tickwise compare a.rec b.rec
```

```bash
tickwise diff a.rec b.rec --at 150
```

Exit codes: 0 identical, 1 different, 2 error. The CLI has no per-command `--help`; use `tickwise --help`.

## How it works

Code lives in `Assets/NetcodeSample/Scripts`, one assembly per layer; dependencies only point down:

```text
Game          runners, scene wiring, input            (Unity, FishNet, everything below)
UI            HannibalUI screens, F1 overlay           (raises events; knows no game code)
Presentation  draws a simulation, hides rollback       (Unity)
Networking    FishNet transport for rollback inputs    (FishNet)
Determinism   Tickwise recording and self-check        (no Unity)
Rollback      rollback session, loopback, sync test    (no Unity)
Simulation    the deterministic game                   (no Unity; DPF core only)
```

**Simulation.** `GameSimulation.Step(red, blue)` advances one 30 Hz tick in a fixed order: free removed cubes, round pause and reset, spawning, target selection, navigation (DPF's flow fields and ORCA avoidance), attacks (all damage applied together, so processing order can't favour a team), base explosions, deaths, round end. State lives in packed blittable structs, so a snapshot (about 1.1 MB, mostly navigation) and a state hash are raw memory copies. Durations are simulated in ticks; cube ids derive from their spawn tick, so a re-simulated spawn keeps its id. Navigation runs behind an interface with a Burst implementation (the game) and a managed one (tests, replays) that produce identical state.

**Rollback.** `RollbackSession.Advance(input)` runs once per fixed tick. Local input applies `InputDelayTicks` (2) ticks later; a missing remote input is predicted as "no button pressed" (button presses are one-shot). Each message carries the sender's unacknowledged inputs, so a lost packet costs nothing. When a remote input differs from the prediction, the session restores the snapshot before that tick and re-simulates to the present. A peer more than `MaxRollbackTicks` (8) ahead of the other's inputs stalls; a peer that is ahead on average skips a tick now and then (frame-advantage time sync, latency cancels out).

**Networking.** The host runs a FishNet server and the joiner is its only client; nothing is synchronized through NetworkObjects. A handshake checks the protocol version and build and hands the joiner its team, the session id and the host's rollback settings. Inputs travel as unreliable broadcasts.

**Presentation.** The presenter observes every simulated tick, including re-simulations, so it always has the corrected positions of the last two ticks and draws cubes between them. When a rollback changes a tick it already showed, the difference becomes an offset that fades out (half-life 50 ms; jumps over 3 m snap), so corrections glide instead of teleporting. Cubes that only existed in a mispredicted timeline fade out; cubes a correction reveals fade in.

**Determinism measurement.** A tick observer hashes (and on dump ticks, dumps) each tick when it's simulated and writes it to the Tickwise recording only when it's confirmed.

## Game rules

All in `Assets/NetcodeSample/Settings/GameRules.asset` (both peers must use the same values; the handshake checks):

| Rule | Small cube | Big cube |
|---|---|---|
| Health | 50 | 100 |
| Damage | 8 | 25 |
| Attack range (edge to edge) | 1 m | 1.5 m |
| Attack cooldown | 0.5 s | 0.25 s |
| Size (radius) | 0.35 m | 0.6 m |
| Speed | 3 m/s | 2.2 m/s |
| Spawning | every 1 s | on input, 5 s cooldown |

Base health 1000, base radius 1.5 m, acquire radius 2 m (a cube turns to fight an enemy within it), 3 s between rounds, up to 256 cubes per team, 30 ticks per second.

**Round time limit, 120 s.** When it runs out, the healthier base wins; with equal health, the side with more cubes; otherwise it's a draw (no point). The HUD shows the clock. Both bases destroyed on the same tick is also a draw. Set *Round Time Limit* to 0 for rounds that only end when a base falls.

Rollback settings are on the *Network Match* (host) and *Local Match* objects; presentation colours, materials and smoothing in `Settings/Presentation.asset`.

## Level

One level, built in the editor and saved with the scene. To rebuild it, run **Netcode Sample > Build Level** with `Main.unity` open. It generates connected rooms with Connected Rooms Generator from the settings and fixed seed in `Level/LevelGeneration.asset`, bakes the NavMesh, converts it to a deterministic DPF navmesh (`Level/DPF-NavMesh.asset`), and stores the bases (red in the start room, blue in the room farthest from it) as fixed-point values in `Level/Level.asset`. The same seed always produces the same navmesh checksum; both peers must have the same one.

## Tests

EditMode tests cover the simulation (determinism, snapshot round trips, Burst/managed equality, spawning, rounds), Tickwise recording and self-check, and rollback (two peers over a lossy network matching a plain simulation, time sync with a slow peer, stalling, sync test).

Run them from **Window > General > Test Runner > EditMode**, or headless with Unity closed:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.11f1/Editor/Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults ./Logs/test-results.xml -logFile -
```

Add `-testFilter <test name or regex>` to run one test. The Tickwise tests need the [native library](#tickwise-local-build).

## Dependencies

Unity Package Manager installs these from `Packages/manifest.json`; `Packages/packages-lock.json` pins every git package's commit.

| Package | Source | Used for |
|---|---|---|
| [FishNet](https://github.com/FirstGearGames/FishNet) | git, tag `4.7.3` | Networking transport |
| [Deterministic Path Finding](https://github.com/cosgunhalil/Deterministic-Path-Finding) | git, `main` | Fixed-point navmesh, flow fields, avoidance |
| [Connected Rooms Generator](https://github.com/cosgunhalil/Connected-Rooms-Generator) | git, `main` | Level generation |
| [HannibalUI](https://github.com/cosgunhalil/HannibalUI) | git, `master` | User interface |
| [UniTask](https://github.com/Cysharp/UniTask) | git, tag `2.5.11` | Required by HannibalUI |
| [Tickwise](https://github.com/cosgunhalil/Tickwise) | git, pinned commit | Recording and comparing simulation hashes |
| [ParrelSync](https://github.com/VeriorPies/ParrelSync) | git, tag `1.5.3` | A second editor for local two-peer testing |
| ProBuilder `6.1.2` | Unity registry | Required by Connected Rooms Generator |
| AI Navigation | Unity registry | NavMesh baking |

Plus [DOTween](#dotween-manual-install) (manual) and the [Tickwise native library](#tickwise-local-build) (local build).

## Known issues

- **Long grinds.** Cubes meet in a front line at the doors and grind each other down; bases usually take damage only once one army collapses, so most rounds go to the time limit and are decided by army size. Two players spawning big cubes at the same rhythm draw.
- **HannibalUI screen switches.** Switching screens again while a switch is running can leave the earlier screen visible (HannibalUI's `VP_NavigationService.SwitchAsync` continues after cancellation). `MatchUI` works around it by queuing switches and hiding leftover screens, so a screen change can take up to a second.
- **Recording location.** The project still has Unity's default company name, so recordings go to `AppData/LocalLow/DefaultCompany/netcode-sample/tickwise`.
- **Windows only.** The Tickwise library is built for Windows x86_64; other platforms need their own build.
