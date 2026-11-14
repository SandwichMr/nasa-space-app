# Project Structure

**Layout:** agreed folder proposal, adapted to an existing Universal 2D template. This is not evidence that the directories or code already exist. Preserve existing Unity/template files. Braces below mean separate child directories, not literal names.

```text
<existing Unity project root>/
  AGENTS.md
  Assets/
    _Project/
      Scenes/
      Runtime/
        Simulation/{Time,State,Resources,Systems,Crew,Tasks,Eva,Events,Equipment,Communications,Evidence}/
        Application/{Commands,Queries,Sessions,Evaluation}/
        Unity/{Bootstrap,Configuration,DataLoading,Presentation,Input,World,Networking}/
      Editor/{Validation,Build}/
      Tests/
        EditMode/{Simulation,Application}/
        PlayMode/
      Data/{Scenarios,Equipment,CrewProfiles,LearningContent}/
      Prefabs/{UI,World,Crew}/
      Art/{Sprites,Textures,Materials,Icons,Animations}/
      Audio/{Music,SFX}/
      Settings/
    StreamingAssets/ScientificData/Episodes/
    ThirdParty/
  Packages/                 # existing Unity package configuration
  ProjectSettings/          # existing Unity project configuration
  docs/
    PROJECT_BRIEF.md
    PROJECT_STRUCTURE.md
    DEVELOPMENT_PRIORITIES.md
    SCIENTIFIC_RULES.md
    research/               # optional larger research; not included in this pack
  tools/data-pipeline/
```

Only create directories when requested or needed. Adding this context pack does not authorize scenes, scripts, assembly definitions or package installation. Unity generates `.meta` files; do not invent their GUIDs.

## Ownership and dependency direction

`Unity adapters -> Application -> Simulation`; Unity adapters may also read simulation types. Simulation never depends on Unity adapters, UI or a networking SDK. Application should remain plain C# too.

- **Simulation:** mutable mission state, units, inventories, power/service models, clock, event queue, task constraints and scenario policies.
- **Application:** player-command validation, session orchestration, read-only observations/queries, objectives and debrief construction.
- **Unity/Bootstrap:** composition and the one runner that advances a session. No duplicate persistent runners after scene reload.
- **Unity/Configuration:** ScriptableObject authoring and conversion to plain definitions. Never mutate shared authoring assets as runtime state.
- **Unity/Networking:** SDK/transport adapters, connections, command submission and snapshots. Networking does not create a second physics/resource model.
- **Unity/Presentation:** UI reads permitted state and sends commands; it cannot directly debit inventories or decide an independent mission outcome.
- **Unity/World:** visual objects/animation. A Transform is not the physical resource ledger.
- **Editor:** editor-only validation/build tooling. Test editor-only assembly settings if assemblies are later introduced.

No assembly definitions are required merely to create folders. If approved later, a minimal arrangement is `Trainer.Simulation`, `Trainer.Application`, `Trainer.Unity`, editor tooling and separate test assemblies. Disable engine references for the plain C# assemblies.

## Data boundaries

`Data/` contains small authoring assets. `StreamingAssets/ScientificData/` is optional runtime data; `docs/research/` is human-readable evidence, not automatically shipped gameplay content. Choose one authoritative source for each scenario and document conversions. Do not duplicate conflicting scenario values across code, UI and assets.

Keep Editor/package versions pinned to the actual project. Track matching assets and `.meta`, `ProjectSettings/`, `Packages/manifest.json` and `Packages/packages-lock.json`. Exclude generated caches/builds with the project's Unity Git rules; do not blindly rewrite its `.gitignore`. Use Git for source collaboration, not Syncthing over a live Unity project/Library folder.
