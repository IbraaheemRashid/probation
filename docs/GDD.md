# PROBATION

*Four interns run the night shift on a surgical ship. Something got loose.*

---

## 1. The pitch

You and three others are the least qualified staff on a small medical vessel parked somewhere nobody visits. Ships dock, unload something injured and not human, and leave. You work out what is wrong with it, you fix it, and you get it out of the door before the shift ends.

You are not good at this. Nobody is watching except each other, and at the end of the night a supervisor reads out, by name, everything you did.

**The horror is not that you might die. It is that it is your fault.**

## 2. What it is, in one loop

```
        ship docks              scan it            write the chart
   patient onto a trolley  →  read the book   →   somebody commits
                                                          │
        quota met                                         ▼
   ◄──  wheel them out   ◄──  close them up   ◄──   twenty seconds
        the same door           and hope           of simple surgery
```

Around that loop, everything goes wrong at once: three patients arrive while you are elbow-deep in one, the only scanner is in someone else's hand, the instruments are dirty and the steriliser is at the other end of the ship, and something you failed to take out of a patient this afternoon is now loose in the waiting room.

## 3. Reference points

| Game | What we take |
|---|---|
| **Lethal Company** | A quota, a shift clock, and a voice channel that carries all the comedy |
| **Overcooked** | Task overload — a competent team is only *just* coping |
| **Papers, Please** | Cross-referencing a document against the thing in front of you |
| **R.E.P.O.** | Physical objects that punish carelessness; carrying is a skill |
| **PowerWash Simulator** | Progress is coverage, not a timer |
| **Shift At Midnight** | Verify each arrival before you serve them - and if you get one wrong, the night turns into something else |

The tone is **friendly horror**: tense, funny, panicked. Nobody dies. Everything is embarrassing.

## 4. Design pillars

**Surgery is a chore.** Twenty seconds, physical, never confusing. The game is the pressure around it, not the task itself. A deep skill mechanic at the centre would eat the attention that belongs on the ward.

**The interesting work is knowing what to do, not doing it.** Depth lives in diagnosis, at a desk, costing time and attention — never dexterity.

**Jobs that occupy a whole player and produce nothing.** Holding pressure on a wound. Standing at the book. Carrying the scanner. This is what makes four players need each other rather than being one surgeon and an audience.

**Everything is physical.** Discharge is a room you push somebody into. A body is a thing you carry. A parasite is an object in your hands. Nothing is a menu.

**Nothing is ever blocked — it is charged.** Wrong tool, wrong procedure, operating on someone awake: all allowed, all costed. A framework that validates and rejects produces a puzzle game, and this is not one.

**The blind surgeon.** Bracing to operate suspends your view entirely. The person with their hands inside a patient is the only one who can see the evidence and the only one who cannot see the room.

## 5. A night

| Phase | Length | What happens |
|---|---|---|
| **Shift** | 210 s | Patients arrive on a tightening timer. Operate, discharge, meet quota. |
| **Cover-up** | 20 s | The doors shut. Anything you half-fixed comes apart. Anything loose is still loose. |
| **Review** | 20 s | The supervisor reads the night back to you, by name. |

**Quota** is 3 on night one, +1 each night. Miss it and you take a strike; three strikes and you are dismissed. The hospital also has a body count — eight deaths ends the run regardless of quota.

A cover-up death does **not** cost tonight's quota, because the quota is already scored by then. It costs the *week*. That asymmetry is the point: it is the thing you thought you got away with.

## 6. Surgery

### The chore

| Procedure | Shape | ~Time |
|---|---|---|
| **Triage** | Already open — clean and close. No incision. | 10 s |
| **Extraction** | Sedate → open → hold open (two people) → take it out → close | 20 s |
| **Brood extraction** | The same, but it does not want to come out. Two people, twice. | 20 s + aftermath |

Three genuinely different shapes, so reading a patient wrong changes what your hands do — not a label on a screen.

### The verbs

- **Sedate** — hold a mask to a face. The one place holding is honest.
- **Open** — one braced drag along the seam. Forgiving; a chore, not a skill test.
- **Hold open** — two people, two retractors, at once. The only structurally two-player moment.
- **Take it out** — grab the thing with forceps and pull. It is now a physical object in the world.
- **Close** — one drag back.

## 7. Diagnosis

Four channels, four prices. **No channel ever gives the answer.**

| Channel | Costs | Gives |
|---|---|---|
| **Presentation** | Free, across the room | The cluster — two or three candidates |
| **Scanner** | One tool, one pair of hands | *This patient's* signs: species, rate, what a scan can see |
| **The patient** | Seconds, and they must still be awake | *Testimony* — their account, which can disagree with the scan |
| **Textbook** | A whole player, standing still, blind | *General* rules: what a body of that species normally contains |

**You get five questions**, and then they have had enough of you. They stop talking the moment you put them under — and every procedure begins by putting them under. So the interview happens before the operation or not at all, while they are frightened and in pain, on a clock.

A patient answers about their condition where they can and falls back on their species where they cannot, and that fallback is the whole trap:

> **"Does it move?"**
> A Thoracid with a mass in its cavity: *"It beats. It has always beaten."*
> A Vithrid with the same reading: *"Nothing in me is supposed to move except my heart, and that sits low."*
> A brood, in either: *"Yes. It turns over when I lie down."*

Nobody is lying. The Thoracid is telling you, truthfully and helpfully, about its own second heart.

The answer exists only in the **join** — and the join happens in somebody's head, out loud, across a ship.

> A Thoracid rests at 68 bpm and has **two hearts**. A Vithrid rests at 112 and has one.
> The scanner says *"dense mass, upper cavity — no movement across repeat scans"* for both.
> Take it out of a Vithrid. Leave it in a Thoracid — that is its heart.

**The chart** at the foot of each bed is the committing act. Nothing can be operated on until somebody walks over and writes down what they think. Their name goes on it, and the review blames *them* for a wrong procedure — never the surgeon who did as they were told.

## 8. The parasite

### The rule that makes it work

**Parasites come from your own failed surgeries.** Not ambient spawns. The thing hunting you tonight is the thing you did not get out of somebody this afternoon. Every night is the bill for that day.

### How one gets loose

| | |
|---|---|
| A **brood** patient left untreated too long | It leaves on its own |
| A brood patient **dies** | It leaves immediately |
| You extract one and **do not incinerate it** | It wakes up in whatever room you left it in |

### What it does

It is **not lethal**. This is friendly horror — the threat is to your *work* and your *dignity*, not your life.

- Reaching a **player** knocks them down. Somebody has to stop what they are doing and come and get you.
- Reaching a **patient** infests them — a simple case becomes a brood, and your night gets worse.
- It moves slowly, it is audible before it is visible, and it goes for whatever is nearest and stationary. A braced surgeon is the most attractive target on the ship.

### How you deal with it — medically

There are no weapons. **The scalpel is the weapon, and so is the gas.**

1. **Sedate it.** The gas rig works on parasites. Costs you the sedative you were going to use on a patient.
2. **Pick it up.** Sedated, it is just an object.
3. **Burn it.** The incinerator, at the far end of the ship, deliberately.

It wakes up if you take too long. Everything you spend on it is something you were going to spend on a patient.

**The incinerator is a room with one door and a six-second cycle**, and that is the whole reason it is not just a second airlock. You commit to walking in, the door is behind you, and you wait beside something you sedated a while ago. Take too long getting there and the cycle is where you find out: one that comes round mid-burn stops the machine and is loose in a small room, between you and the way out.

The **airlock** takes bodies — one gesture, instant, no ceremony. The difference between the two is the point.

### The escalation

| Stage | Where | What it costs you |
|---|---|---|
| **1** | In the scan — you can see it is there | Nothing yet. You still have every option. |
| **2** | In the waiting room — in your space | You have to stop what you are doing. |
| **3** | On the table — in your hands, mid-operation | Somebody is open in front of you. You cannot walk away. |

A parasite still loose when the shift ends is **found**, and goes in the review under the name of whoever last had it.

## 9. The ship

Small on purpose — 30 × 20 m, everything within five seconds. The ward this replaced was 42 × 34 and a night was mostly walking.

```
   ┌─ DOCK ──────────┐═══ north corridor ═══┌ CLEANING ┐ BRIDGE
   │  ▣▣▣  discharge │                      │          │  (dead
   ├────── door ─────┤                      │          │   end)
   │  WAITING        ├═══════ SPINE ════════┴──────────┴───┐
   ├────── door ─────┤                                     │
   │  SURGERY [1][2][3] ═══ south corridor ═══ AIRLOCK ────┘
   └─────────────────┘         │
                          INCINERATOR (one door)
```

**The Dock is the only way in or out for anybody alive. The Airlock is the only way out for anything you would rather was not found.** They are at opposite ends: the busy end and the guilty end.

Arrivals and departures share the Dock and one 2 m doorway — wide enough for one trolley plus somebody squeezing past, and not two.

The layout is a **figure-eight**: two loops sharing the Waiting-to-Spine segment. Every room has two exits except the Bridge, which is the one place you have to commit to entering. That matters the moment something is chasing you — a map of dead ends is not a chase, it is an execution.

**Instruments are split.** A working set in Surgery so routine work is local; the steriliser and spares in Cleaning at the far end. The long trip is the price of a mistake, not a tax on every patient.

## 10. Interface

**In-game UI is deliberately almost nothing.** Everything that can be a physical object in the world is one.

| On screen | Why it earns its place |
|---|---|
| A crosshair and an interact prompt | You cannot infer what is grabbable |
| The current step of the operation you are standing at | Your hands are full of the tool it is naming |
| Shift clock, quota, strikes | The shared clock the whole team is playing against |
| Announcements — six lines, fading | The ship telling everyone at once |
| The scanner panel, **only while holding the scanner** | It is a readout on a tool you chose to carry |
| The textbook, **only while reading it** | Same |

Nothing else. No inventory, no minimap, no objective list, no health bar. If a player needs to know something, they should have to *look at something* or *ask somebody*.

**Menus** are a different matter and get to be a real interface: main menu, host or join, and the end-of-week verdict.

## 11. What the review says

The one screen the whole game exists to produce. Every system records who did what as it goes:

```
The supervisor reads the night back to you.

  Intern 1   charted a patient for extraction (x3)
             cut open a Thoracid to take out its second heart
  Intern 2   completed the extraction - patient survived (x2)
             slammed a patient into something
  Intern 3   left a specimen on the ward floor
             picked up the scalpel (x9)
```

Attribution rules are load-bearing: wrong-**procedure** harm belongs to whoever wrote the chart; wrong-**tool**, awake-operation and impact harm belong to the hands. Blaming the surgeon for the diagnostician's call would poison the only screen that matters.

## 12. Scope

**Built:** the ship, the shift and its phases, patients with species and conditions, the scanner, the chart, three procedures, wrong-procedure consequences, post-op fragility, the cover-up crash window, the review, discharge and the morgue, physical carrying and hauling, bracing, the steriliser, four-player networking over Steam.

**This pass adds:** the parasite, the main menu, and whatever else one complete, playable night needs.

**Deliberately later:** the textbook, cutting as a real drag rather than a hold, supplies that run out, the bridge doing anything, nights 2 through 7, voice.
