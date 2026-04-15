# ForgeGPU Roadmap

## Purpose

ForgeGPU is evolving from a queue-based inference demo into a resource-aware orchestration platform demo.

The point is no longer just background processing or asynchronous job execution. The next architectural direction is explainable scheduling under resource constraints:
- which jobs enter the system first
- how fairness is preserved across cost classes
- why a machine is eligible or ineligible
- why a particular machine is chosen
- how live execution state stays understandable while durable state remains trustworthy

This roadmap describes the next major evolution toward an actor-oriented, resource-aware, fair scheduling model with future Kafka ingress band topics.

## Core Architectural Principles

The next phases should follow these principles explicitly.

### Durable and live state separation

- Postgres is the durable source of truth for durable entities and configuration.
- Redis is a live projection and fast read model, not the durable source of truth.
- Actor-owned in-memory state is the authoritative live execution state.

### Control-plane ownership

- Kafka will be used later as ingress transport only.
- Workers and machines must not consume directly from Kafka.
- A central `CoordinatorActor` owns assignment decisions.
- `MachineActor` owns machine resource state and execution state.

### Weight handling

- Bands are ingress grouping only.
- Exact weight remains on the job and is used in scheduling, fairness debit, and resource accounting.
- Coarse ingress grouping must not replace exact execution math.

## Terminology

### Exact weight

The exact numeric job weight stored on the job record. This remains the real scheduling and accounting signal even when jobs are grouped into coarse ingress bands.

### Weight band

A coarse range bucket derived from exact weight, used only to group ingress traffic into understandable scheduling lanes.

### Ingress band

The runtime lane, queue, or topic associated with a weight band. It is a pull source for the `CoordinatorActor`, not an execution owner.

### Capacity unit

A simulated scheduling unit representing machine execution capacity. Capacity units are not real GPU metrics; they are an explainable demo abstraction used to model contention and placement.

### Effective cost

The estimated cost of placing a job on a machine, expressed in effective capacity units. It is derived from job attributes such as exact weight, required memory, model, and optional fake CPU or GPU coefficients.

### Eligible machine

A machine that satisfies all hard admission constraints for a job:
- enabled
- supported model
- enough remaining capacity units
- enough worker slots
- enough simulated RAM, VRAM, and CPU admission

### Best-fit eligible machine

Among machines that satisfy all hard constraints, choose the one that leaves the smallest non-negative remaining capacity after placement, to reduce fragmentation and preserve larger machines for heavier jobs.

### Deferred job

A job that has entered the system but cannot currently be placed on any eligible machine. Deferred jobs are reconsidered later through the coordinator reconciliation loop.

### Live projection

A fast, shared, ephemeral view of current state published from actors to Redis. It is intended for visibility, coordination support, and dashboard reads, but not for durable truth.

### Machine catalog

The durable machine inventory stored in Postgres. It contains machine definitions, capabilities, and configuration, not transient execution ownership.

### CoordinatorActor

The lightweight actor-oriented control-plane component that owns ingress pull, fairness decisions, machine selection, and deferred-job reconciliation.

### MachineActor

The lightweight actor-oriented component that owns the live state of one machine: reservations, running jobs, resource usage, heartbeat, batching, and execution state.

## Weight-Band Taxonomy

ForgeGPU should not open one ingress queue or topic per exact weight.

That would create a one-topic-per-weight explosion, complicate reasoning, and make the system harder to explain. Instead, ForgeGPU should use coarse range-based ingress bands while preserving exact weight on every job.

### Planned ingress bands

- `1-2w`
- `3-5w`
- `6-10w`
- `11-20w`
- `21-40w`
- `41w+`

### Why this banding model exists

- avoids one-topic-per-weight explosion
- keeps ingress lanes understandable
- small jobs stay granular
- larger jobs are grouped more coarsely
- exact weight is still preserved for real scheduling and accounting

### Future Kafka topic names

- `forgegpu.jobs.w1_2`
- `forgegpu.jobs.w3_5`
- `forgegpu.jobs.w6_10`
- `forgegpu.jobs.w11_20`
- `forgegpu.jobs.w21_40`
- `forgegpu.jobs.w41_plus`

## Scheduling Model

ForgeGPU should evolve toward a two-level scheduling model.

### Level A: Fair pull across bands

The `CoordinatorActor` should consume ingress jobs into internal band buffers.

Important constraints:
- the coordinator consumes ingress traffic
- workers and machines do not consume directly from Kafka
- fairness is decided centrally
- exact weight remains visible and usable at decision time

#### Preferred fairness algorithm

Use Deficit Round Robin (DRR) or an equivalent explainable fair-pull algorithm.

Recommended behavior:
- each band accumulates credit over time
- selecting a job from a band spends credit
- the debit amount is the exact job weight
- the coordinator rotates fairly across bands instead of draining only the lightest jobs

#### Goal of the fair pull layer

- avoid draining only the lightest jobs
- avoid starving heavier jobs
- preserve fairness across cost classes
- maintain explainability when a job was chosen from one band instead of another

### Level B: Capacity-aware machine assignment

After the coordinator selects a job from the fair-pull layer, it should assign that job to a machine.

#### Hard constraints for eligibility

A machine is eligible only if all of the following pass:
- machine is enabled
- model is supported
- enough remaining capacity units exist
- enough worker slots exist
- enough simulated RAM, VRAM, and CPU admission exists

#### Selection rule

- filter eligible machines
- choose the best-fit eligible machine
- apply a deterministic tie-break

This preserves explainability and reduces fragmentation while still allowing future policy refinement.

## Actor-Oriented Control Plane

ForgeGPU is moving toward a lightweight actor-inspired design.

The design should stay intentionally simple:
- actor-style mailboxes and channels
- actor-owned state
- explicit message flow
- no heavyweight external actor framework

### CoordinatorActor responsibilities

- own global scheduling decisions
- pull from ingress bands
- maintain internal band buffers
- run the fairness policy
- inspect machine snapshots
- assign jobs to machine mailboxes
- retry deferred jobs on reconciliation ticks

### MachineActor responsibilities

- own machine live state
- own reservation and release of capacity units
- own reservation and release of VRAM
- own running job set
- own execution mailbox
- publish live state projection to Redis
- emit heartbeats
- participate in batching and reliability behavior

### Why this control-plane model matters

This model makes ownership explicit.

- The coordinator owns global scheduling.
- Each machine owns its own resource and execution state.
- Redis becomes a shared projection, not a hidden source of truth.
- Future Kafka ingress can be added without changing who owns execution.

## Machine Catalog

Machines should become durable entities in Postgres.

### Durable machine catalog fields

At minimum, the machines table should store:
- `machine_id`
- `name`
- `enabled`
- `total_capacity_units`
- `cpu_score`
- `ram_mb`
- `gpu_vram_mb`
- `max_parallel_workers`
- `supported_models`
- `created_at_utc`
- `updated_at_utc`

### Demo machine examples

ForgeGPU should keep at least five heterogeneous fake machines for the demo story, for example:
- 15 units
- 20 units
- 17 units
- 5 units
- 12 units

These are simulated machine profiles for demo scheduling and dashboard visualization. They are not claims about real hardware discovery.

## Live State Projection

`MachineActor` should publish live state into Redis.

This is a projection, not durable truth.

### Recommended Redis fields

- `actorInstanceId`
- `lastHeartbeatUtc`
- `actorStatus`
- `usedCapacityUnits`
- `remainingCapacityUnits`
- `activeJobCount`
- `reservedVramMb`
- `runningJobIds`
- `currentBatchSize`
- `currentModels` when useful

### Explicit state separation

- Postgres = durable machine catalog
- Redis = live shared projection
- MachineActor memory = authoritative live state

This separation matters because the system needs both:
- a durable definition of what machines exist
- a fast shared view of what actors are doing now
- a clear execution owner that prevents ambiguous state authority

## Heartbeat and Liveness

Machine liveness should be explicit and operationally explainable.

### Requirements

- each `MachineActor` publishes heartbeat periodically
- heartbeat interval should be configurable
- Redis keys should use TTL
- stale heartbeat means machine is unavailable for new assignments
- a stopped actor does not mean the machine row is deleted
- actor down means the live execution endpoint is unavailable, not that the durable machine definition is gone

### Graceful shutdown path

On graceful shutdown, the actor should publish an `Offline` or equivalent state before exiting.

### Crash path

On crash or abrupt loss of liveness, TTL expiry should cause the machine to become `Stale` or `Unavailable`.

### Coordinator responsibility

The `CoordinatorActor` must exclude stale or unavailable machines from assignment decisions.

## Resource Estimator

ForgeGPU needs a `ResourceEstimator` that computes:
- `EffectiveCostUnits`

### Inputs

Inputs may include:
- exact weight
- `requiredMemoryMb`
- model
- optional fake CPU and GPU coefficients

### Roadmap guidance

In early phases, the estimator should remain simple and explainable.

The goal is not realism for its own sake. The goal is a defensible placement model that humans and coding agents can reason about easily.

## Deferred and Reconciliation

If no machine is eligible:
- the job becomes deferred
- deferred jobs are reconsidered periodically
- re-evaluation happens through `CoordinatorActor` reconciliation ticks

Future phases may add durable deferred state, but this roadmap does not assume that problem is already solved.

The important architectural rule is that deferred reconsideration stays coordinator-owned.

## API and Dashboard Visibility

ForgeGPU should expose the control plane clearly.

### Planned visibility surfaces

- `GET /machines`
- existing `/workers` integration where useful
- existing `/metrics` integration where useful

### Future dashboard sections

- ingress band depths
- consumer lag by topic
- internal band buffer depth
- machine cards
- scheduler decision stream
- dispatches by band
- completed by band
- deferred by band
- fairness summary
- utilization summary
- machine utilization heatmap
- deferred job visibility

### Practical dashboard intent

The dashboard should show:
- what each ingress band currently contains
- consumer lag by topic
- internal band buffer depth
- which machines are available or saturated
- what resources are reserved on each machine
- which decisions the coordinator is making
- dispatches by band
- completed by band
- deferred by band
- where fairness credit is accumulating or being spent
- which jobs are deferred and why
- how machine utilization trends across the fleet, including a heatmap view

This should remain practical and operational, not vague marketing UI.

## README Strategy

The README should later include a section called:

`Resource-aware fair scheduling`

That section should explain:
- why bands exist
- why exact weight is preserved
- why workers do not consume directly from Kafka
- why best-fit assignment is used
- how actor-style machine ownership keeps state consistent and explainable

### Example to include later

A 17-unit machine may receive `10w + 5w + 1w + 1w`, instead of draining only `1w` jobs or only medium jobs, to balance fairness and resource utilization.

This example should connect the fairness layer and the best-fit assignment layer clearly.

## Phase Plan

### Phase 9.0 / 9.1

- actor-oriented control plane foundation
- `CoordinatorActor` + `MachineActor`
- durable machine catalog in Postgres
- live machine state projection in Redis
- machine-centric resource-aware assignment
- `GET /machines`

### Phase 9.2

- weight-band taxonomy adoption
- bands:
  - `1-2w`
  - `3-5w`
  - `6-10w`
  - `11-20w`
  - `21-40w`
  - `41w+`
- exact weight preserved on job metadata

### Phase 9.3

- fairness algorithm across bands
- Deficit Round Robin or equivalent
- exact-weight debit
- starvation avoidance
- best-fit machine assignment refinement

### Phase 9.4

- Kafka ingress migration
- topic names exactly as listed above
- workers remain indirect consumers only
- `CoordinatorActor` is the only ingress consumer

### Phase 9.5

- dashboard
- queue band visibility
- consumer lag by topic
- internal band buffer depth
- machine cards
- scheduler decision stream
- dispatches by band
- completed by band
- deferred by band
- fairness and utilization summary
- machine utilization heatmap
- README `Resource-aware fair scheduling` section

### Phase 10

- recruiter polish
- final README storytelling
- architecture diagrams
- benchmark summary
- CV-ready positioning

## Open Design Notes

- bands are coarse on purpose
- exact weight still drives debit and resource accounting
- Kafka is ingress only, not execution ownership
- actor death means machine unavailable, not machine deleted
- Redis liveness projection is intentionally ephemeral
- later phases may introduce data-driven adaptive scheduling, but not yet

## Roadmap Outcome

If this roadmap is followed, ForgeGPU will evolve into a recruiter-friendly orchestration platform demo that can explain:
- how jobs enter the system
- how fairness is enforced across cost bands
- how exact weight remains meaningful
- how capacity-aware placement works
- why machine ownership belongs to actors
- why Postgres, Redis, and Kafka each have different roles
