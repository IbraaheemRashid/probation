# Player controller — setup and tuning

Floating-capsule rigidbody controller for PROBATION. Client-authoritative by design:
each client simulates its own intern, the host owns world objects.

## Scene setup

Build this hierarchy once, then save it as `Assets/Prefabs/Player.prefab`.

```
Player                      (root — NEVER rotates)
├─ Rigidbody                mass 70, Freeze Rotation ON, Interpolate,
│                           Collision Detection = Continuous Dynamic,
│                           Linear/Angular Damping left at 0 (the scripts own damping)
├─ CapsuleCollider          height 1.4, radius 0.30, center (0,0,0)
├─ PlayerInputReader        ← drag Assets/InputSystem_Actions.inputactions into Action Asset
├─ PlayerLocomotion         ← set Ground Mask to everything EXCEPT the Player layer
├─ PlayerInteractor
└─ CameraPivot              localPosition (0, 0.65, 0)
   ├─ PlayerLook            owns yaw AND pitch
   ├─ Camera                localPosition (0,0,0), FOV 70
   └─ HandAnchor            localPosition (0.25, -0.20, 0.45) — tools parent here later
```

The root origin is the **centre of the capsule**, not the feet. At rest it floats
`standRideHeight` (0.95 m) above the floor, so the capsule bottom sits ~0.25 m up and the
eye lands at ~1.60 m. That 0.25 m gap is free step-over height: cables, kerbs, dropped
instruments and low door sills are climbed by the spring with no step logic at all.

**Put the player on its own layer** and exclude that layer from Ground Mask, or the probe
will find the player's own capsule and the spring will fight itself.

## Why the body never rotates

A capsule is rotationally symmetric, so body yaw carries no information. Freezing it means
look input never has to travel through the physics tick — `PlayerLook` runs in `Update` at
full framerate and stays sharp no matter what the fixed timestep is. This sidesteps the most
common jitter bug in Unity FPS controllers (mouse look applied to an interpolated rigidbody).

When a visible body mesh is added, rotate the *mesh* to `PlayerLook.Yaw` in `LateUpdate`.
Never rotate the rigidbody — except during `Knockdown`, which is exactly why knockdown works.

## Tuning values

| Knob | Start at | Notes |
|---|---|---|
| Rigidbody mass | 70 | Every force below is derived from this. Change it and re-tune the spring. |
| `rideSpring` | 30000 | N per metre. At mass 70 this sags ~2.3 cm at rest. |
| `rideDamper` | 3000 | Critical damping here is ~2900. Below ~2000 the player visibly bobs. |
| `standRideHeight` | 0.95 | Raise for more step-over, lower for a heavier feel. |
| `probeExtra` | 0.30 | How far past ride height we still count as grounded. Doubles as ledge tolerance. |
| `walkSpeed` | 3.2 | Corridors are tight; faster than ~4 feels wrong indoors. |
| `sprintSpeed` | 5.2 | |
| `groundAcceleration` | 45 | Reaches walk speed in ~0.07 s. Lower it for a heavier, more comic body. |
| `airAcceleration` | 10 | ~22% of ground control. |
| `jumpHeight` | 0.9 | Low on purpose. Jump is for comedy, not traversal. |
| `fallGravityMultiplier` | 1.8 | Kills the floaty arc. |
| `encumberedSpeedMultiplier` | 0.45 | Speed at `Encumbrance = 1`. |

Set **Fixed Timestep to 0.0166** (Project Settings ▸ Time) — 60 Hz physics noticeably improves
spring feel over the 50 Hz default, and costs nothing at this scale.

## Feel problems and what causes them

| Symptom | Cause |
|---|---|
| Player bobs or bounces at rest | `rideDamper` too low relative to `rideSpring`. |
| Spring oscillates violently | `rideSpring` too high for the fixed timestep. Lower it or raise the tick rate. |
| Sinks into the floor on landing | `probeExtra` too small — the probe loses the ground during the fall. |
| Slides down gentle ramps | `maxSlopeAngle` too low, so the ramp is being treated as a wall. |
| Camera jitters while walking | Rigidbody interpolation is off, or something is writing to the root's rotation. |
| Snags on door frames | Capsule radius too large, or the frame's collider is not convex. |

## Encumbrance

`PlayerLocomotion.Encumbrance` (0–1) is the hook the carry system drives. 0 is empty-handed;
1 is both hands on a patient. It scales top speed and acceleration together, which is what
makes hauling a body across the ward feel like work rather than like walking with a prop.

## Knockdown

`PlayerLocomotion.Knockdown(duration, impulse)` unfreezes rotation so the body genuinely
tumbles, suspends the spring and all steering, then locks upright again on recovery. This is
the entry point for a gurney to the shins, a mistimed defib, and an Outbreak swipe — and the
first half of "a downed intern is just a patient with a voice".

## Netcode plan (not yet implemented)

Decided model: **client-authoritative players, host-authoritative world.**

- Install `com.unity.netcode.gameobjects`. The Multiplayer Center package is already present.
- On a remote player: disable `PlayerInputReader`, `PlayerLocomotion`, `PlayerLook`,
  `PlayerInteractor`, and set the Rigidbody to `isKinematic`. Only the owner simulates.
- Replicate the root position plus `PlayerLook.Yaw` and `Pitch` (a client-authoritative
  NetworkTransform, or a small custom one at ~20 Hz with interpolation).
- Gurneys, patients, corpses, doors and tools stay host-owned rigidbodies. Grabbing an object
  requests ownership transfer from the host; the host is the arbiter when two people grab the
  same thing.
- No prediction, no reconciliation. It is a four-friend co-op game — a cheater can fly, and
  nobody cares. This is the single largest schedule saving available on this project.

## Next

1. `PlayerCarry` — two-handed pickup via a configurable joint on `HandAnchor`, driving
   `Encumbrance`. Do NOT parent held objects to the hand; joints keep them in the physics
   simulation, which is where the comedy lives.
2. Surgery stance — a `Surgery` action map where the mouse drives the *tool* rather than the
   camera. Add the map to `InputSystem_Actions` now; retrofitting map-switching is painful.
3. Footstep audio driven off `IsGrounded` and horizontal speed — this is also the input for
   proximity-chat volume and, later, for the species that wakes when the room gets loud.

---

# Phase 1 — Multiplayer spine

**Goal: four people in one room, talking.** Client-authoritative players, host-authoritative
world. No prediction, no reconciliation.

## Order of operations

1. **Install Netcode for GameObjects first.** Window ▸ Package Manager ▸ Unity Registry ▸
   search "Netcode for GameObjects" ▸ Install. `NetworkBootstrap.cs` and
   `PlayerNetworkSetup.cs` reference `Unity.Netcode` and the project will not compile until
   the package resolves.
2. `Probation ▸ Setup ▸ 4 - Network the Player` — adds `NetworkObject`, `PlayerNetworkSetup`
   and two `NetworkTransform`s to the prefab, creates a `NetworkManager` with
   `UnityTransport` + `NetworkBootstrap`, and registers the Player prefab.
3. Save the scene.
4. **Verify by hand:** both `NetworkTransform`s should read **Authority Mode = Owner**. NGO
   renamed that serialized field between minor versions, so the setup script tries three
   spellings and logs which one worked. If it warned, set it yourself.
5. Build (File ▸ Build Profiles ▸ Build) and run the build alongside the Editor. Editor hosts
   on `127.0.0.1`, build joins. That is a real two-client test on one machine.

## What "done" looks like

- Four clients connect and each sees three other capsules moving.
- Every client's own camera is the only one enabled — no "2 audio listeners" spam.
- Your cursor stays locked when other players spawn (this is what `CursorLock` exists for).
- Shoving a crate looks the same on all four screens.
- Voices fall off with distance.

## Then Relay

Direct IP only works on a LAN. For friends in other houses, add Relay:

- Install `com.unity.services.relay`, `com.unity.services.authentication`, `com.unity.services.core`.
- Link the project to a Unity Gaming Services project (Edit ▸ Project Settings ▸ Services).
- `UnityServices.InitializeAsync()` then `AuthenticationService.Instance.SignInAnonymouslyAsync()`.
- Host creates an allocation and gets a join code; clients join with the code.
- `NetworkBootstrap`'s address field becomes a join-code field. Nothing else changes.

The transport is swappable without touching gameplay, which is why Steam sockets can replace
Relay at Early Access as a one-component change.

## Then voice

Dissonance (Asset Store) with its NGO integration. Proximity and occlusion out of the box.
Give it four days; if it is not running end to end by then, switch to Vivox rather than
nursing it — voice that is "nearly working" for three weeks taxes every other phase.

## Known constraint

`PlayerNetworkSetup` disables remote players' `PlayerLocomotion` and sets their Rigidbody
kinematic. That means **remote interns cannot currently be pushed** — they are puppets driven
by `NetworkTransform`. Player-to-player shoving needs the host to arbitrate, and that belongs
in Phase 2 alongside object ownership transfer. Don't try to fix it here.

---

# Authority model

## The rule

> **An object has exactly one authority, at all times.**
>
> Multi-player interaction happens either by several players sending *forces* to one
> host-owned object, or by several players owning *different* objects that jointly determine
> host-owned state. **Never two owners on one object.**

Two owners on one Rigidbody has no clean solution in NGO (see issue #2558), and no shipped
game does it. R.E.P.O. avoids it with a grab beam — the object is host-simulated and each
player's grab is a spring force, not a claim. Surgeon Simulator 2 and Space Station 13 avoid
it by splitting the work so each player manipulates a different object.

## Who owns what

| Thing | Authority | Why |
|---|---|---|
| Your own movement | Owner | Feel. No prediction needed. |
| Held tools | **Transfers to holder** | Surgery is precision work; RTT lag on a scalpel is fatal. |
| World objects at rest | Host | One truth about where the gurney is. |
| Patients | **Host, always** | Everyone's score depends on it. |
| Corpses, gurneys | **Host, always** | Multi-grab has to work. |
| Attribution log | **Host, always** | It is the payoff scene. It cannot be client-reported. |
| A downed player | **Transfers to host** | They stopped driving and started being operated on. |

The last row is the fiction and the netcode agreeing: going down means losing authority over
your own body.

## Two grab systems, not one

This is the part that catches people out. There is no single grab mechanic that works for
both a scalpel and a patient.

**`HeldTool` — ownership transfers to the holder.**
For single-hand precision items: scalpel, forceps, retractor, scanner, gas rig.
- On grab, request `NetworkObject` ownership from the host.
- `NetworkTransform` Authority = **Owner**. `NetworkRigidbody` handles kinematic switching.
- Joint to `HandAnchor`, never parenting.
- **Predict the grab locally.** Snap the tool to your hand immediately and let the host's
  answer arrive after. If someone else won it, it snaps back. Waiting a full round trip
  before a pickup visibly responds feels broken.

**`GrabBeam` — host keeps ownership, grabs are forces.**
For heavy and shareable things: patients, corpses, gurneys, trolleys, crates.
- Client sends a target point; the host applies a spring force toward it.
- `NetworkTransform` Authority = **Server**, permanently.
- Any number of players can grab at once; the physics resolves the tug-of-war.
- The object lagging behind your hand is indistinguishable from the spring being springy.
  Latency reads as weight — this is why R.E.P.O. feels good rather than laggy.

Objects declare which they are. Nothing uses both.

## Consequences for procedures

**Procedure evaluation runs on the host only.** Clients own their tools and therefore ship
tool transforms; the host reads them and decides when a step completes. Play local VFX and
audio immediately as cosmetic prediction, but host state is the truth.

**Every completion condition needs a tolerance band.** The host is always evaluating slightly
stale tool positions, so exact contact tests will feel unreliable to clients. Design steps
around "close enough for long enough", never "touched this pixel".

**The two-person spreader must be two objects.** One object with two owners is the trap. Give
it two handles, each owner-authoritative, and have the host read the gap and angle between
them to decide whether the shell splits or cracks. Identical for the player; trivial for the
netcode.

## What the host being a player means

The host has zero latency on its own tools; everyone else has RTT. In a competitive game that
would matter. Here it does not — but host the person with the best connection, not whoever
opened the game first.
