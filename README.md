# My Test Assignment — Active Ragdoll

**English** · [简体中文](README.zh-CN.md)

Unity developer test assignment: implement an **Active Ragdoll** system for a humanoid with **hit reactions** and a **Balance** mechanic. Smooth transitions between animation and physics-driven states.

**Reference games:** *Exanima*, *Hellish Quart*, *No Rest for the Wicked* (camera style).

**Brief:** [`docs/Unity_测试任务.pdf`](docs/Unity_测试任务.pdf)

> **Status:** In progress — update checkboxes below as features land.

## Demo

<!-- Add GIF/screenshot: light hit, heavy hit, knockdown, recovery, boulder -->

## Requirements

| Item | Notes |
|------|--------|
| Unity Editor | **6000.3.13f1 (LTS)** — mandatory |
| Environment | Primitives OK for level geometry |
| Character | Model & animations provided (`Assets/Models/`) |
| Submission | GitHub repo (preferred) or `.zip` |
| Tested platform | Windows *(update if needed)* |

Do **not** commit `Library/`, `Temp/`, `Logs/` (see `.gitignore`).

## Getting Started

1. Clone this repository.
2. Open in Unity Hub with **6000.3.13f1**.
3. Open `Assets/Scenes/SampleScene.unity` (or the main test scene once created).
4. Press **Play** — use **Group 1** area for hit reactions, **Group 2** for knockdown, **boulder path** for instant knockdown.

First open may require several minutes of asset import.

## Controls

| Action | Input |
|--------|--------|
| Move | `W` `A` `S` `D` (or on-screen virtual joystick) |

Top-down camera; no combat input required on the player — damage comes from dummies and the boulder.

Input asset: `Assets/InputSystem_Actions.inputactions` (extend for virtual joystick if targeting mobile).

## Assignment Overview

### Player

- Movement: WASD / virtual joystick
- Animations: **Idle**, **Run**, **Get Up** (from back / from front)
- **Balance:** 6 points; regen after **1.5–2 s** without being hit
  - Light hit → **−1** balance
  - Heavy hit → **−2** balance
  - At **0** → **Knockdown**, then balance resets to full

### Scene test layout

| Zone | Purpose |
|------|---------|
| **Group 1** | Hit reaction test — 2 stationary dummies, attacks on a **3 s** timer |
| **Group 2** | Knockdown test — 2 dummies, **continuous** light/heavy attacks; far from Group 1 |
| **Boulder** | Sphere on a **looping path**; collision → **instant knockdown** (ignores balance); force along boulder velocity |

**Group 1**

- Dummy 1 — light attack every 3 s  
- Dummy 2 — heavy attack every 3 s  

**Group 2**

- Dummy 3 — light attacks, no delay  
- Dummy 4 — heavy attacks, no delay  

### Combat mechanics (summary)

| Type | Behavior |
|------|----------|
| **Light hit** | Upper-body directional flinch; blend with current anim; feet planted; no ragdoll; recover idle in **&lt; 0.3 s** |
| **Heavy hit** | Partial ragdoll on struck limb chain (e.g. shoulder → arm + upper torso); impulse in hit direction; stagger on rest of body; physics blends back in **~0.5–1 s** |
| **Knockdown** | Full ragdoll; inherits final hit force/direction; light KO = small crumple; heavy KO = launch; force at **contact point**; natural settle (no canned fall anim) |
| **Recovery** | After ragdoll settles: detect face-up / face-down (spine up · world up); pose-matched blend into **Get Up From Back** or **Get Up From Front**; return to idle |

Tuning of balance damage, regen delay, and impulse values is allowed for better feel.

## Features Checklist

### Core systems

- [ ] Active ragdoll (animation ↔ physics handoff)
- [ ] Light hit reaction (4-way upper body, &lt; 0.3 s recovery)
- [ ] Heavy hit (partial ragdoll + stagger + blend back)
- [ ] Balance (6 pts, regen, knockdown at 0, reset after KO)
- [ ] Full knockdown + contact-point impulses
- [ ] Recovery (pose detect + matched get-up + idle)

### Player & scene

- [ ] Player movement (WASD / optional joystick)
- [ ] Idle / Run animations
- [ ] Top-down camera (*No Rest for the Wicked* style)
- [ ] Group 1 dummies (timed light / heavy)
- [ ] Group 2 dummies (continuous light / heavy), spaced from Group 1
- [ ] Boulder on loop path, instant KO on player collision

### Polish & delivery

- [ ] README demo media
- [ ] Standalone build (optional but recommended)
- [ ] GitHub submission ready

## Project Structure

```
Assets/
  Scenes/              # Main test scene
  Models/              # Provided character / animation_pack.fbx
  Prefabs/             # Player, dummies, boulder
  Scripts/             # Ragdoll, balance, hits, AI timers (add here)
  Settings/            # URP
docs/
  Unity_测试任务.pdf    # Official brief (EN + ZH)
```

## Design Notes

*(Document your approach as you implement.)*

- Suggested modules: `BalanceSystem`, `HitReactionController`, `RagdollController`, `RecoveryController`, dummy attack drivers, boulder mover.
- Hit direction: front / back / left / right for light flinch blending.
- Heavy hit: per-limb or chain ragdoll activation + `ConfigurableJoint` / `Rigidbody` impulses.
- Knockdown: distinguish last hit was light vs heavy for collapse vs launch.
- Recovery: dot(spineUp, worldUp) for supine vs prone get-up clip.

## Known Limitations

- *(List intentional scope cuts or tuning still in progress.)*

## Third-Party Assets

| Asset | Location | Notes |
|-------|----------|--------|
| Character / animations | `Assets/Models/` | Provided by assigner — document if external license applies |
| Unity packages | `Packages/manifest.json` | URP, Input System, etc. |

## Author

<!-- Replace with your details -->
**Your Name** — [email@example.com](mailto:email@example.com)
