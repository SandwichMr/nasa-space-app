# Project Brief

## Confirmed

- Challenge: **Build a Junior Astronaut Mission Trainer**, NASA Space Apps 2026.
- Engine/template: **Unity Universal 2D**, C#; the owner describes a nearly empty existing project.
- **Multiplayer is required by the owner**, not by the official challenge.
- Intended audience: children aged **7–12**.
- Goal: a playable prototype where managing competing outpost needs teaches a genuine engineering/survival constraint.

The exact Editor version, package inventory, folder existence and implementation state have not been inspected for this context pack. The 2D template does not settle the camera, art style or projection.

## Challenge alignment

Manage a lunar or Martian outpost; decisions about life support, radiation shielding, power, food production and communication delay should have understandable consequences. Use a focused playable trade-off instead of attempting every subsystem in full detail.

The format is open. NASA data, clear attribution, low access barriers, replayability and a manageable number of visible resources are encouraged. A live API, online multiplayer, a specific camera, a full crop simulator or every mechanic in the research are not mandatory challenge implementations.

Official detail: https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/

## Preliminary concept, not approved final design

A small already-installed lunar base and a short exterior route:

`Habitat -> airlock -> work area -> science instrument`.

Players cooperate to activate an instrument, obtain measurements and return while retaining essential-service reserves. The base has electricity/storage, life-support equipment, supplies, thermal services and a protection area. Each modeled object must have a purpose.

Mission loop:

1. Inspect objective, stocks, equipment and dated observations.
2. Share tasks and choose a resource plan.
3. Execute; confront one understandable contingency.
4. Finish or safely cancel; review the actual costs and alternatives; retry.

Example trade-off: science and water treatment compete with essential electrical demand. A battery can bridge a deficit but spending reserve changes later options. Cooperation should emerge from dependencies, not arbitrary simultaneous button presses.

Start with power and air as primary UI information; expose other details progressively without merging O2 and CO2 into misleading physics. Safe cancellation can be competent play. Roles should be interchangeable and should not leave a participant idle.

## Suggested first slice

Two connected players, a small base, one science task, one energy decision and one bounded contingency. Begin the network proof with a shared electrical load before final art. Two players and the Moon are scope recommendations, not confirmed final limits/destination.

No initial open world, free construction, vehicles, extensive crafting, multiple planets or medical simulation. Stored food is honest for a short scenario; crop growth must retain biological time scales if added later.

## Open decisions

Final destination/site; camera and presentation; online versus other multiplayer configuration; final player count and roles; hosting/transport/service; exact Unity version; delivery platform; mission duration and time compression; actual hardware, stocks and numerical inputs; science objective and contingency. Ask only when an unresolved choice blocks the requested work; do not settle all of them during a folder-only task.
