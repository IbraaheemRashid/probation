# Getting a night running

## Once, in the editor

Open `Assets/Scenes/Map.unity` and run these from the **Probation** menu, in order:

| | |
|---|---|
| `Setup > 9 - New Map Scene` | Only if `Map.unity` does not exist yet. It wipes the scene. |
| `Setup > 10 - Build The Ship` | The rooms, the volumes, the trolleys, the instruments, eight patients, four parasites and the waypoint graph. |
| `Verify and Repair Scene` | Should report nothing missing. |

**Do not run `Setup > 6`** on this scene — it places instruments at the old ward's coordinates, which land in the void between Surgery and the Airlock. Step 10 places its own.

Then save the scene.

## Playing

Press Play. You get the title card.

- **HOST A SHIFT** — needs Steam running. Everyone else accepts an invite.
- **START THE NIGHT** — the host opens the doors once everybody is aboard.

Solo, `autoHost` is on, so you land straight in the lobby and can start immediately.

## One night, start to finish

**Before the doors open.** You are in the Landing Dock. Three trolleys are parked in intake and three more in the Waiting Room. The instruments you need are on the bench in Surgery; spares and the steriliser are in Cleaning, at the far end of the ship.

**The night is 210 seconds and the quota is 3.**

### The loop

1. **A patient is on a trolley in the dock.** Somebody fetches the **scanner** from the Cleaning bench and reads them: species, heart rate, whether they can feel this, and what a scan can see.
2. **Somebody writes the chart** at the foot of the bed. Press E to cycle: *triage*, *extraction*, *brood extraction*, *no operation*. Their name is on it now.
3. **Wheel them to a berth** in Surgery. Nothing progresses until they are inside one.
4. **Run the procedure.** The step you need is on screen while you stand near them.
5. **Wheel them back to the dock** and into the discharge half. `DISCHARGED 1/3`.

### What you have to know

Two species, and they invert each other:

| | Thoracid | Vithrid |
|---|---|---|
| Resting heart rate | 68 | **112** |
| Bleeds out in | 45 s | **20 s** |
| Metal instruments | fine | **harm them** |
| Upper cavity | **two hearts** | one |

> The scanner says *"dense mass, upper cavity — no movement across repeat scans"* for both.
> **Take it out of a Vithrid. Leave it in a Thoracid — that is its heart**, and cutting it out kills them after they walk out of the door.

A mass that **moves** between scans is not an organ and not a foreign body. It is alive.

### The parasite

A brood patient has something in them. You have about **55 seconds** before it stops waiting.

- **Get it out properly** and it arrives already sedated, in your hands. Carry it to the **incinerator** - a small room with one door, off the south corridor. The machine takes six seconds, and it stops if the thing wakes up inside it.
- **Leave it too long, or let them die,** and it lets itself out.

Loose, it is slow, it is audible before it is visible, and it goes for whoever is least able to leave — patients first, and a **braced surgeon** above anybody else, because they have suspended their own view to work.

It will not kill you. It knocks you down, and somebody has to stop what they are doing and come and pick you up. Reaching a patient is worse.

**To deal with one:** hold a **gas rig** near it. Same object you sedate patients with — which is the cost. Then pick it up and carry it to the incinerator. The **airlock takes bodies, not living things**, and will tell you so.

It wakes up after about 22 seconds, so putting it down somewhere and forgetting is not a plan.

### The end

**Cover-up, 20 s.** The doors shut and the quota is already scored. Anything you half-fixed comes apart now, and **anything still loose on the ship gets found**. The discharge door stays open — you can still save somebody.

**Review, 20 s.** The supervisor reads the night back, by name.

## What is not built

The textbook (species knowledge lives in your head or in this file for now), cutting as a real drag rather than a hold, supplies that run out, and the bridge doing anything at all.

`R` restarts the week from the verdict screen.
