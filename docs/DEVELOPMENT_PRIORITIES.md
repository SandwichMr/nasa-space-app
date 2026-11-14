# Development Priorities

This is the compact, updated delivery order. It supersedes the old roadmap's recommendation to postpone multiplayer: multiplayer is now required. It is a plan, not implemented functionality or a test report. Do not execute these phases unless the user requests implementation.

## P0 — Small, complete cooperative prototype

1. **Scenario and tooling contract:** confirm the playable trade-off and necessary assumptions. Inspect/pin the actual existing Editor/project before choosing packages. Make a smoke build on the approved target early.
2. **Shared-session proof:** two clients join one session, request a load change and observe one consistent result. Choose the backend only with approval. Prove real connectivity before final art or large maps.
3. **Authoritative physical core:** one clock, resource ledger, capacities, electrical bus/battery, stable event ordering and explicit units. Views and clients cannot debit independent copies.
4. **Power plus life support:** connect essential/optional loads and a bounded crew/O2/CO2 service model. Optional science should change the reserve trajectory. Demand is applied once.
5. **Playable loop now:** start, inspect, act, see consequences, finish/cancel, debrief and retry. Use placeholders; keep two or three decisions visible at once.
6. **Supplies and tasks:** potable versus untreated water, food stock, explicit modeled outputs and exclusive crew/equipment assignments. Add only the processes the scenario needs.
7. **Bounded EVA and contingency:** a return route, ingress time, limiting services/reserve and one understandable authored event or repair. No operational/medical safety claim.
8. **Submission hardening:** source/assumption labels, readable alerts and controls, disconnect behavior, target build, reset flow, demo instructions and regression checks.

Networking and physics are dependencies, not a reason to delay the first playable loop indefinitely. Build one thin cooperative slice before expanding.

## Multiplayer contract

- One authority owns physical state/time. Clients submit commands; the authority validates resources, ownership and applicability.
- Stable action IDs/handling prevent duplicate consumption. Exclusive work/equipment cannot be claimed twice.
- Shared pause/speed/restart policy; no independent client clock advancement.
- Handle client loss deliberately; for an initial host-based prototype, host loss can end/interrupt the session with an explanation. Do not promise host migration/reconnect recovery before implementation.
- Private room codes, temporary names and quick pings are recommendations. Voice, public matchmaking and cross-platform support are not implied requirements.
- NASA datasets can be packaged while multiplayer still needs connectivity. A local fallback does not prove online networking works.
- Earth-to-base delay is a simulated domain property, not general lag imposed on teammates.

## P1 — Improve a working slice

Coupled thermal behavior; better sensing/diagnosis; site-based lighting; contamination/airlock accounting; richer activity/rest planning; contact windows; source-availability-aware replay; additional debrief alternatives. If a P0 claim already depends on a service, model/bound it there rather than pretending its dependency does not exist until P1.

## P2 — Later depth

Separate Mars calibration, crop cohorts, industrial ISRU, detailed chemical recovery, long logistics, complex rescue, free expansion and advanced generation. None is needed to prove the first trade-off.

## Evidence-gated, regardless of available coding time

Human dose/shielding numbers require valid response/transport data and geometry. Clinical risk prediction is outside scope. Solar forecasting needs a defined target, sufficient data, temporal validation and calibration. Authored events/replay must not be labeled operational forecasts.

## Acceptance checks when code exists

Write and run focused tests for conservation; nonnegative/capacity-limited resources; battery power versus energy; no double demand; pause/event timing; step-refinement behavior; command idempotence; conflicting multiplayer actions; restart; and the complete mission flow. Test two actual clients and the built target, not only the Editor.

Record actual commands/results or execution blockers. Numeric repeatability is not scientific validation; same seed does not guarantee bitwise cross-platform identity. Exact Unity test/build commands depend on the installed Editor, license, modules and packages and must be discovered, not invented.
