# Scientific Rules — Compact Guardrails

Read when changing simulated resources, hazards, crew behavior or data. This file is not a parameter dataset, medical guidance or flight-certified model.

## Evidence and parameters

Classify inputs: observation; engineering requirement; contextual design reference; derived calculation; explicit scenario assumption; or unknown. Keep units, source/version, context, validity and uncertainty. Unknown is not zero. Test fixtures may use labeled synthetic values; do not silently turn them into scientific production defaults.

No universal O2 rate, recovery efficiency, clinical threshold, EVA protocol, failure rate or repair time may be inferred from a single published context. Retrieve the relevant source before introducing a physical number. Existing SCOPE citations use section-qualified IDs; `02:S1` and `06:S1` need not refer to the same source.

## Physical accounting

- One owner applies crew demand and transfers. Model boundaries, inputs, outputs, residuals and losses explicitly. Conserve mass/energy for the modeled system; cancelling work does not refund already used resources.
- Power (W) differs from energy (J or explicitly converted kWh). Adequate battery energy does not guarantee available discharge power.
- O2 inventory/supply, pressure, circulation and CO2 removal are distinct. A fan neither creates oxygen nor eliminates the cabin's total CO2 inventory. A well-mixed zone is a declared simplification.
- Potable water differs from captured/untreated water. Processing is limited by feedstock, quality, throughput, energy and hardware. A measured ISS recovery percentage is not a universal whole-base efficiency.
- Food, food moisture, nutrients, waste and crop biomass are different stocks. Do not count food moisture twice or generate an instant harvest. Compression of presentation time must not independently accelerate biology.
- Explicit seconds, mass, energy, power, absolute temperature and pressure units; conversions at boundaries. Select numerical resolution with event/convergence checks, not rendering FPS.

## Lunar setting and crew

Outside the Moon's habitat there is no breathable atmosphere and no useful ambient convective cooling. Interior gas can transfer heat; external rejection/conduction require their own model. Cold surroundings do not guarantee a cool powered machine.

Light/shadow depends on site, geometry and epoch. Do not create a full lunar day/night cycle every few minutes without honest time compression and a defined scenario. Low gravity reduces weight, not mass or inertia. Suit return must account for travel, ingress, limiting service and declared reserve; no universal safe EVA radius or prebreathe recipe.

Missing/stale readings are not zero or normal. Keep physical truth separate from information available to the player. Engineering exposure criteria are not deterministic disease/instant-death thresholds.

## Space weather

Flare, CME, SEP and GCR are different phenomena. Kp, X-ray class and proton flux are not interchangeable with human dose. Earth GOES or ISS detector readings are not automatically local lunar/martian astronaut measurements.

Keep Gy/Sv, detector material, equivalent/effective dose, observation location and integration time distinct. No shielding percentage or human dose without validated transport/response inputs, spectrum, geometry and a defined output quantity. Use clearly qualitative training states until those dependencies exist. Rest does not erase accumulated exposure; an event ending does not automatically prove all-clear. A particle alert does not itself reduce panel illumination.

## Data and communication

Separate measured time, provider publication/revision, receipt and simulation time. Historical replay is not forecasting. Do not expose a future peak/end time simply because a retrospective catalogue contains it. Label authored/synthetic events and packaged data honestly; static data is not live. Attribute actual NASA sources and check distribution rights before shipping datasets.

Earth-to-base propagation is distance/c in each direction, with contact/queue/processing/human delay added separately. It is not the lag between nearby crew players. Do not exaggerate lunar propagation into a long Martian delay without an explicit educational label.

## Deep references — only when needed

The full research is deliberately excluded from this small pack. If provided separately, place `SCOPE.md` under `docs/research/` and open only relevant sections: C/annex 01 radiation; D/02 life support; E/03 energy/environment; F/04 crew/EVA; I/05 data/models; G/06 operations; J validation. Its selective audits do not certify a game.

Useful primary starting points:

- Life-support BVAD Rev. 2 (2022), with contextual tables: https://ntrs.nasa.gov/api/citations/20210024855/downloads/BVAD_2.15.22-final.pdf
- NASA human-system standard portal; confirm applicable edition/context: https://www.nasa.gov/reference/nasa-std-3001v2/
- NOAA phenomenon definitions: https://www.swpc.noaa.gov/phenomena/solar-radiation-storm

If needed evidence is unavailable, identify the gap and narrow the claim. Do not fabricate precision to make a feature look finished.
