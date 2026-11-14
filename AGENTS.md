# AI Working Guide

## Project in one paragraph

Unity **Universal 2D**, C#, multiplayer game for NASA Space Apps 2026: **Build a Junior Astronaut Mission Trainer**. Children aged 7–12 should understand resource trade-offs by managing an outpost. The current concept is preliminary: a small lunar base, shared resources, a science objective and safe crew return. The Moon, camera, exact player count and mission are proposals, not final approvals. Multiplayer and Universal 2D are confirmed.

## Read selectively

Read this file first. Then open only what the current task needs:

- File placement, folders, architecture: `docs/PROJECT_STRUCTURE.md`.
- Gameplay, requirements, unresolved choices: `docs/PROJECT_BRIEF.md`.
- Work order and acceptance gates: `docs/DEVELOPMENT_PRIORITIES.md`.
- Resource models, environment, NASA data: `docs/SCIENTIFIC_RULES.md`.

Do not load every document by default. Do not recursively scan `Library/`, `Temp/`, `Obj/`, builds, all assets or the entire repository. Inspect the task's paths and dependencies first; broaden only when necessary. Existing research documents are optional deep references, not required startup context.

## File routing

Project-owned content: `Assets/_Project/`.

- `Runtime/Simulation/`: plain C# physical state, clocks, resources, tasks and events. No UnityEngine or networking SDK dependency.
- `Runtime/Application/`: plain C# commands, sessions, queries and mission evaluation.
- `Runtime/Unity/`: Unity configuration, bootstrap, input, presentation, world and networking adapters.
- `Editor/`: editor-only tooling. `Tests/EditMode/` and `Tests/PlayMode/`: tests.
- `Data/`, `Prefabs/`, `Art/`, `Audio/`, `Settings/`: authoring/assets.
- `Assets/StreamingAssets/ScientificData/`: optional packaged datasets.
- Root `docs/`: documentation. Root `tools/data-pipeline/`: offline tooling.

## Non-negotiables

1. Follow the current user request. Folder-only means directories only: no scripts, scenes, `.asmdef`, packages or placeholders. This documentation pack does not authorize implementation.
2. Inspect existing files before editing; preserve template assets, renderer settings, `.meta` relationships and existing workflows. Do not create a nested Unity project.
3. Do not choose packages, Editor upgrades, network services, a camera or a deployment platform without approval. No networking backend is selected yet.
4. One session authority advances the simulation. Clients send validated intentions; views do not maintain separate inventories. Prevent duplicate consumption, conflicting interactions and independent client clocks.
5. Keep mass/energy accounting, units, capacities and event timing explicit. No fake clinical thresholds, flare-to-dose conversions or instant crops.
6. Do not represent proposals as implemented features or tests as passed without actual output. Editor version, packages, code and folder existence must be checked in the real project; they were not audited when this pack was prepared.
7. Run relevant available checks after code changes. If Unity/license/modules prevent execution, state the blocker; never invent test/build results.
8. Keep changes task-scoped. No repository publication, service changes or unrelated cleanup without authorization. Never place credentials or personal data in assets/docs.

## Keep this guide small

After an approved architectural change, update the relevant short document and this routing summary only if necessary. Do not append chat logs, full research, speculative backlogs or routine execution histories here.
