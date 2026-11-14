# Development Priorities — Five Playable Phases

**Current plan:** exactly five cumulative phases. Each closes with an integrated, visible, playable build and real multiplayer validation, not independent code alone. This replaces the old P0/P1/P2 delivery sequence and the historical six-plus-preparation phase numbering. SCOPE remains the scientific reference, not the current delivery calendar.

## 1 — Playable cooperative power room

A simple visible 2D base room, battery, essential/science loads, station selection, two connected clients, objective, result and retry. Basic sprites/prefabs and a working scene are required now. No avatar movement or physiology is required yet. Reuse the five existing implementation branches; review and integrate them inside this phase before calling it complete.

## 2 — Playable base management

Simple controllable 2D crew, interior stations, exclusive tasks, bounded air services, water quality and food stocks. Players divide time and power while completing an interior mission. No instant harvest or clinical claims.

## 3 — Playable exterior mission

Small exterior, airlock, task, optional science objective, return margin and effective protection. Add a clearly labeled authored or contextualized archived particle alert, not unsupported human dose or forecasting.

## 4 — Playable mission operations

One understandable contingency, inspection/repair/test, dated observations and deferred Earth support. Add bounded food-production planning with actual resource/time cost; yields require valid data and biological time. Keep a short mission honest if no harvest occurs.

## 5 — Playable release candidate

Polish the already-working game: tutorial, visual/audio consistency, accessibility, readable debrief, sources/licenses, target build, actual multiplayer group/topology testing and demo. This is not when graphics or integration begin.

## Five parallel workstreams in every phase

D1 simulation; D2 gameplay/composition and sole shared-scene integrator; D3 multiplayer; D4 2D art/UI/prefabs; D5 validators, tests and builds. Use one frozen base commit/API and separate owned files. Roles can build against agreed contracts/fakes, but fakes do not prove the integrated mission.

Review and merge the five parts into `integration/phase-NN`, wire the scene in a separately authorized integration task, remove production fakes and test the same build. Integration is part of the current phase, not a sixth phase. Contracts/global dependencies change only through a coordinated approved update.

## Existing strict assignments

They remain valid isolated implementation tasks until explicitly replaced. This new roadmap does not grant running agents permission to edit protected files/scenes. Finish/review their scoped outputs, then authorize the integration and missing visual work needed to close phase 1. Do not throw away correct code or rewrite the shared base without inspection.

## Non-negotiable validation

Conservation, explicit units/capacities, no duplicate resource consumption, one clock/authority, stable epochs/revisions, command idempotence, permitted actions, coherent restart and visible disconnection behavior. Compare steps/tolerances where relevant; test the actual player target and real clients. Report blockers and real commands/results. No fabricated test output or cross-platform guarantees.

No timeline estimate, final camera, final player count, network SDK or deployment target is settled by this plan. The owner reports matching LTS/modules; inspect actual versions. Scientific gates for human dose, clinical risk, unvalidated forecasting and industrial technology remain in force.

The expanded plan is `docs/DEVELOPMENT_ROADMAP.md`, with five team handoffs under `docs/roadmap/` when the new roadmap pack is installed. Read only the phase/role needed. This documentation update does not implement, merge or publish game code.
