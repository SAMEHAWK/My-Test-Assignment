# My Test Assignment — Active Ragdoll

**English** · [简体中文](README.zh-CN.md)

Unity developer test assignment: implement an **Active Ragdoll** system for a humanoid with **hit reactions** and a **Balance** mechanic. Smooth transitions between animation and physics-driven states.

**Reference games:** *Exanima*, *Hellish Quart*, *No Rest for the Wicked* (top-down camera style).

## Foreword

This project implements the full active ragdoll pipeline: WASD movement → light hit flinch → heavy hit partial physics → knockdown full ragdoll → recovery. The core architecture uses a hierarchical state machine + dual-skeleton physics and is currently functional.

**Known areas for improvement:**
- **Heavy hit feel**: The blend-back smoothness of partial ragdoll, force propagation attenuation across hit chains, and the transition between stagger animation and physics still have room for tuning. A satisfying parameter set hasn't been found yet.
- **Animator module**: The current Animator Controller's layer structure was stacked incrementally as features were added, with layer weight management scattered across the code. The plan is to refactor it into a standalone animation module that consolidates layer switching, CrossFade scheduling, and overlay lifecycle management.
- **Hit feedback**: `CharacterHurtbox` already reserves per-limb damage/impulse multipliers, but they haven't been utilized for differentiated body-part response yet; there's room to improve hit feel with layered feedback.

Most of these directions are currently at the "rough idea" stage with no concrete solution yet.

All code and documentation were completed with AI assistance (Cursor + Deepseek V4 pro), following a "plan → confirm → implement" collaboration workflow.

## Requirements

| Item | Notes |
|------|--------|
| Unity | **6000.3.13f1 (LTS)** — mandatory |
| Tested Platform | Windows |

## Getting Started

1. Clone this repository.
2. Open in Unity Hub with **6000.3.13f1**.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Press **Play** — the scene contains two groups of dummies and a boulder: Group 1 for hit reactions, Group 2 for knockdown, and the boulder path for instant knockdown.

First open may require several minutes of asset import.

## Controls

| Action | Input |
|--------|--------|
| Move | `W` `A` `S` `D` |
| Equip / Unequip | `E` |
| Light Attack | Left Mouse Button |
| Heavy Attack | Right Mouse Button |

Top-down camera; the player can actively attack dummies and will also take damage from dummies and the boulder.

Input asset: `Assets/InputSystem_Actions.inputactions` (extend for virtual joystick if targeting mobile).

## Assignment Overview

### Player

- Movement: WASD
- Animations: **Idle**, **Run**, **Get Up** (from back / from front)
- **Balance:** 6 points; regen after **1.5–2 s** without being hit
  - Light hit → **−1** balance
  - Heavy hit → **−2** balance
  - At **0** → **Knockdown**, then balance resets to full

### Scene Test Layout

| Zone | Purpose |
|------|---------|
| **Group 1** | Hit reaction test — 2 stationary dummies, timed attacks |
| **Group 2** | Knockdown test — 2 dummies, continuous attacks; far from Group 1 |
| **Boulder** | Sphere on a **looping path**; collision → **instant knockdown** (ignores balance); force direction = boulder velocity at impact |

**Group 1**

- Dummy 1 — light attacks
- Dummy 2 — heavy attacks

**Group 2**

- Dummy 3 — continuous light attacks
- Dummy 4 — continuous heavy attacks

### Combat Mechanics (Summary)

| Type | Behavior |
|------|----------|
| **Light hit** | Upper-body directional flinch; blend with current anim; feet planted; no ragdoll; recover idle in **&lt; 0.3 s** |
| **Heavy hit** | Partial ragdoll on struck limb chain (e.g. shoulder → arm + upper torso); impulse in hit direction; stagger on rest of body; physics blends back in **~0.5–1 s** |
| **Knockdown** | Full ragdoll; inherits final hit force/direction; light KO = small crumple; heavy KO = launch; force at **contact point**; natural settle (no canned fall anim) |
| **Recovery** | After ragdoll settles: detect face-up / face-down (spine up · world up); pose-matched blend into corresponding Get Up animation; return to idle |

Tuning of balance damage, regen delay, and impulse values is allowed for better feel.

## Features Checklist

### Core Systems

- [x] Active ragdoll (animation ↔ physics handoff, dual-skeleton architecture)
- [x] Light hit reaction (4-way upper body, &lt; 0.3 s recovery)
- [x] Heavy hit (partial ragdoll + stagger + blend back)
- [x] Balance (6 pts, regen, knockdown at 0, reset after KO)
- [x] Full knockdown + contact-point impulses
- [x] Recovery (pose detect + matched get-up + idle)

### Player & Scene

- [x] Player movement (WASD)
- [x] Idle / Run animations
- [x] Top-down camera
- [x] Group 1 dummies (timed light / heavy)
- [x] Group 2 dummies (continuous light / heavy), spaced from Group 1
- [x] Boulder on loop path, instant KO on player collision

### Extended Features (Beyond Original Scope)

- [x] Player active attacks (light / heavy, with root motion and hit detection)
- [x] Weapon equip / unequip system (with dynamic animation layer switching)

## Project Structure

```
Assets/
  Scenes/                  # Main test scene
  Models/                  # Character model / animation_pack.fbx / extracted clips
  Prefabs/                 # Player prefab
  Scripts/                 # Core character system scripts
    Character/             # Character controller, state machine, sub-modules
      Config/              # ScriptableObject config definitions
      Debug/               # Debug tools (hit injection, balance display)
      Editor/              # Editor extensions
      Modules/             # Animation playback controllers (equip, attack)
    Ragdoll/               # Ragdoll system (chain definitions, bone mapping, config)
    Combat/                # Combat system (hurtboxes, weapon hit scanning)
    Gameplay/              # Gameplay mechanics (rolling boulder)
    Camera/                # Camera follow
    UI/                    # UI utilities
  Animator/                # Animator Controller
  Configs/                 # ScriptableObject config assets
  Settings/                # URP render pipeline configuration
```

## Architecture Overview

The character controller uses a **Hierarchical State Machine (HSM) + Module Composition** architecture:

- **3 superstates / 9 substates**: `Grounded` (Locomotion / WeaponEquipPlayback / AttackPlayback), `HitReaction` (LightFlinch / HeavyStagger), `Incapacitated` (Knockdown / ForcedKnockdown / Recovering)
- **Dual-skeleton ragdoll**: VisualModel (animation skeleton) + RagdollRig (hidden physics skeleton), with world-space pose write-back
- **5 sub-modules**: CharacterMotor (movement), CharacterCombat (balance), CharacterAnimationPresenter (animation), CharacterRagdollSystem (ragdoll), CharacterRecoveryFlow (recovery)
- **Config-driven**: All movement, balance, animation, and ragdoll parameters are configured via ScriptableObjects

See `docs/` for detailed architecture documentation.

## Third-Party Assets

| Asset | Location | Notes |
|-------|----------|--------|
| Character / animations | `Assets/Models/` | Provided by the assigner |
| Greatsword animation set | `Assets/MassiveGreatSword_AnimSet/` | Third-party animation assets |
| Unity packages | `Packages/manifest.json` | URP, Input System, Cinemachine, etc. |

## Author

**王燿增** — [916821412@qq.com](mailto:916821412@qq.com)
