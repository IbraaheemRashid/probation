# Getting set up from a fresh clone

1. **Unity 6000.3.7f1** (Unity Hub will offer to install it when you open the project).
2. Open the project. The first import takes several minutes while `Library/` is rebuilt from
   scratch - this is normal, don't kill it.
3. Open `Assets/Scenes/Greybox.unity`.
4. Press Play.

That is all. Netcode for GameObjects comes from the package manifest, the Steam transport is
vendored in `Packages/` (patched - see its `PATCHES.md`), and `steam_appid.txt` is in the repo.
Nothing needs installing by hand.

## Two ways to connect

**Direct IP** (top-left panel) - no Steam required. `127.0.0.1` to test the Editor against a
local build. Use this for everyday iteration.

**Steam** (second panel) - needs Steam running and logged in. Host lobby, then Invite friends
to open the Steam overlay. This is how real playtests happen. It needs two machines: you cannot
run two Steam clients on one PC.

## If something looks broken

- **No `Probation` menu in the menu bar** - the project does not compile. Editor scripts never
  loaded. Scroll the Console to the *first* `CS####` error, not the loudest one.
- **Burst "Failed to resolve assembly Assembly-CSharp-Editor"** - cascade noise from a compile
  failure. Fix the real error and it goes away.
- **Steam panel says "unavailable"** - Steam is not running, or you are not logged in.
- Press **F3** in play mode for the diagnostics overlay.

---

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

---

# Connecting people

`NetworkBootstrap` uses UnityTransport with a direct address. What that reaches:

| Scenario | Address to enter | Works? |
|---|---|---|
| Two windows, one PC | `127.0.0.1` | Yes |
| Same house / same wifi | Host's LAN IP (`192.168.x.x`) | Yes |
| Over the internet, raw | Host's public IP | Only with UDP 7777 port-forwarded, and never behind CGNAT |
| Over the internet, via VPN | Host's Tailscale IP (`100.x.x.x`) | Yes, no code changes |
| Unity Relay | Join code | Yes — needs UGS project membership |
| Steam sockets | Steam lobby | Yes — the shipping answer |

## Right now: Tailscale

Fastest way to playtest with friends this week. Everyone installs [Tailscale](https://tailscale.com),
joins the same tailnet, and the host reads off their `100.x.x.x` address. Everyone else types it
into the address field. Zero code changes, no port forwarding, no router access, works through
CGNAT. Free for small groups.

ZeroTier does the same job if anyone objects to Tailscale.

## Then: Relay

The planned Phase 1 finish. Blocked until the Unity account is a member of the `borgicy` org —
see the cloud project note. Replaces the address field with a join code and nothing else changes.

## Eventually: Steam sockets

The shipping answer, and what Lethal Company uses. Free at any scale, NAT punching handled by
Steam, no service accounts. Use the Facepunch transport for NGO. You can develop against
Spacewar (app ID 480) before you have your own app ID.

# Things that will bite during playtests

- **Everyone must run the identical build.** NGO version-checks on connect and a mismatch fails
  with an unhelpful error. Rebuild and redistribute together, every time.
- **There is no host migration.** If the host quits or crashes, everybody drops and the run is
  gone. Host the most stable machine. This is also a real design problem for a 45-minute run —
  worth solving before Early Access, not before the slice.
- **Windows Firewall** prompts the first time you host. Allow it on private networks.
- **Default port is UDP 7777.**
- **Test with real latency early.** Everything feels perfect at 0 ms on localhost. The first
  session with someone on 60 ms is when you find out which interactions need wider tolerance
  bands. Do that inside Phase 1 — not in Phase 4 when five procedures are already built on
  assumptions formed at zero latency.

---

# Phase 2 — grabbing

Two grab mechanics, chosen per object by `Grabbable.kind`. Neither ever puts two owners on one
Rigidbody. Nothing is ever parented to the hand: parenting removes the object from the physics
simulation, and the physics is where the comedy lives.

## `GrabKind.Tool`

Scalpel, forceps, retractor, bone saw. Ownership transfers to the holder, so the tool responds
at local framerate — round-trip lag on a blade would gut the surgery minigames.

- Client asks; the **host** decides and calls `ChangeOwnership`. One pair of hands at a time.
- Held by spring force and torque toward `HandAnchor`, applied by the owner in `FixedUpdate`.
- Released back to the host on drop, so an idle tool has a stable authority.
- `NetworkTransform` authority: **Owner**.

## `GrabKind.Heavy`

Patient, corpse, gurney. The host keeps ownership permanently and every grab is a spring force.

- Any number of interns can grab at once; the physics resolves the tug-of-war.
- The host reads each grabber's hand position **straight off the replicated player transform**,
  so hauling costs no extra network traffic at all.
- Each grabber pulls from the point they actually grabbed, in object-local space.
- `NetworkTransform` authority: **Server**, always.

## Known simplification

Grabs are **not predicted locally** yet. Picking up a tool waits one round trip for ownership,
which is fine on LAN and noticeable over Steam. The plan calls for optimistic local prediction
with a snap-back if the host refuses; `PlayerCarry.TryGrab` is where that goes. Deliberately
left out to keep the first version readable.

## Attribution

`IncidentLog` (host-only) already records pickups. Every system from here that could produce a
review line should record one as it happens — see the note in the slice plan about why this is
a phase 2 concern rather than a phase 6 one.

## Setup

`Probation ▸ Setup ▸ 6 - Add Grabbable Props` builds a table, four tools and a gurney plus a
patient into the current scene. They are scene NetworkObjects, so the host spawns them
automatically - no prefab registration needed.

---

# Phases 3-6 — patient, procedures, shift

## The patient (phase 3)

`Patient` is simulated by the host and nobody else. Everyone's shift score depends on it, so
none of it can be client-reported.

- States: `Stable → Bleeding → Critical → Dead`, driven by a single `harm` value.
- `ApplyHarm(amount, byClientId, reason)` — **every caller names the intern responsible**.
  That argument is what the review screen reads out.
- **Death is not a fail state.** It is a logged incident, and the body stays in the world as a
  `Heavy` grabbable that somebody has to wheel somewhere.
- `VitalsMonitor` synthesises its beep at runtime, so it works with no audio assets. Positional
  with a 14 m range: you can hear that another room is going badly without seeing why.

`Species` is a ScriptableObject. This is how the game gets variety without new systems — the
same procedures behave differently because the patient's rules changed.

## Specialisms (phase 3)

`PlayerRole` grants **information, never stats**. Stat classes make one player the good one;
information classes force everyone to talk, which is the only reason to build a voice game.

| Specialism | Sees |
|---|---|
| Anaesthesia | whether the patient is actually conscious |
| Xenobiology | the real diagnosis |
| Vascular | can close bleeds permanently |
| Exostructure | gates steps that need a carapace opened |

Chosen per shift at the locker (bottom-left HUD panel). Never permanent, never assigned, and
two people may pick the same one.

Values are replicated to everyone and gated at *display* time. Hiding them properly would need
targeted RPCs, which is not worth it against four friends who can see each other's screens.

## Procedure framework (phase 4)

A `Procedure` is an ordered list of `ProcedureStep`. A step knows four things: which tool,
which site, what counts as done, and how it fails.

**A step never refuses an input.** There is no "is this allowed" check anywhere in `Operation`.
Wrong things are allowed to happen and then have consequences — a framework that validates and
rejects produces a puzzle game, and this is not one.

Every test is a tolerance band: **near enough, for long enough**. The host judges slightly stale
tool positions, so exact contact tests feel broken to everybody who is not hosting.

`handsRequired` is what makes co-op structural rather than decorative. The extraction's
"hold the seam open" needs **two** — and the scene ships **two retractors**, because two hands
means two owner-authoritative objects, never one object with two owners.

## Second procedure (phase 5)

`Procedure_Suture.asset` is a new asset and a new tool. No new code. That was the test.

## The shift (phase 6)

`ShiftDirector` runs seven days of ~6.5 minutes with a review between each. The run-ending
condition is the **hospital's** body count, never an individual's — interns are never removed
from a run, because benching somebody for the remaining half hour is how you lose the group.

The review groups `IncidentLog` by intern and reads it back. This is why attribution had to be
threaded through from phase 2.

## Not yet built

- Sabotage (deliberately: phase 2 already permits it, and nothing prevents it)
- The emergency ladder — Hiccup / Code / Incident / Outbreak
- Patient spawning and intake; the scene has one hand-placed patient
- Write-ups and the Orderly demotion
- Voice

---

# Cut: specialisms, lockers, clock-in

Removed deliberately. The GDD had four specialisms granting private information, chosen at a
locker during a clock-in phase. All three are gone.

**Why.** None of the games this is chasing have classes - Lethal Company, R.E.P.O., PEAK and
Meccha Chameleon all let everyone do everything. Classes add a failure mode those games avoid
on purpose: turn up without an anaesthetist and the lobby is blocked before it starts. That is
the opposite of what four friends want from an evening.

**What replaced it.** The thing specialisms protected - *the only way through is to talk* - now
comes from **instruments**. There is one scanner. Whoever holds it can read the patient's state,
pain and diagnosis, and is therefore not holding a scalpel. Same asymmetry, sourced from an
object rather than a character sheet, which is better in every direction:

- no lobby composition problem
- it can be dropped, taken, lost, or hidden by somebody being a nuisance
- carrying it costs you a hand, so scanning is a real decision
- it needs no UI to explain and no phase to assign

Steps still gate on `handsRequired`, which is what makes co-op structural. They no longer gate
on who you are.

The shift now begins immediately: `Shift -> CoverUp -> Review -> next night`.

---

# The loop, rebuilt for pressure

Two problems the research surfaced, and what was done about them.

## 1. There was no reason to hurry

Lethal Company's engine is the quota - the clock is only frightening because you owe something.
We had a clock and nothing behind it: you could stand still for the whole night and it ended
identically.

**Quota.** Discharge N patients alive, rising each night (`baseQuota` + `quotaGrowth`). Miss it
and you take a strike; three strikes or eight deaths and the ward closes.

**Discharge is physical.** Treating is not enough - the patient has to be wheeled to the
discharge bay, and the dead have to go to the morgue. This is what the heavy-haul system was
always for, and it makes the last thirty seconds of a night a scramble.

## 2. The ward was under-loaded

Overcooked's postmortem names the failure: players settle into comfortable roles and stop
talking. Their three fixes, applied here:

**Task overload.** Six beds, eight pooled patients, intake tightening from a 26s gap to 9s
across the night. Beds are positions and patients are objects that move between them, so a
finished patient still occupies a bed until somebody physically moves them - the ward silting
up with bodies nobody has wheeled out *is* the pressure.

**Time delays.** `Operation` no longer wipes progress when you walk away. Half-finished work is
picked up by whoever arrives next, which is what lets one intern run four beds instead of
standing at one. A deteriorating patient still undoes it slowly, so abandoning a bleeder
remains a decision with a cost.

**Disruptions.** `ComplicationDirector` fires Hiccups (a bleed opens on somebody nobody is
watching) and Codes (a patient crashes and needs hands now), seeded per night so two groups can
compare the same night.

## The washing up

`Steriliser`. A used instrument is soiled, and a soiled instrument fails procedure steps until
somebody has walked it to the steriliser. This is Overcooked's dirty plates: a chore that never
stops, forces people across each other's paths, and makes "somebody grabbed the wrong scalpel"
something that emerges rather than something the game punishes.

## Playing a full run

Nights are 3.5 minutes while the loop is being tuned, so seven of them is about half an hour.
At `WEEK OVER` the host presses **R** to start a new week without re-hosting.

---

# Information costs an action

A design rule, not a UI preference.

**Anything about a patient must be looked up.** Condition, heart rate, whether they are awake,
what is actually wrong with them - none of it is on screen by default. You pick up the scanner,
you point it at somebody, and your hands are full while you do. Whoever knows is therefore not
the person operating, which is why they have to say it out loud.

**Stamina is the one exception**, because it is your own body and you would feel it. It fades in
only once spent and is absent at full, so ordinary movement never feels rationed.

The operation panel is not an exception: it shows which tool goes where and how far through the
step you are - procedure information, not patient information. It never leaks condition.

This is why the monitor cart matters. It is the only way to know a patient is deteriorating
without standing over them, there is one of it, and it has to be wheeled to whoever you have
decided is worth watching.
