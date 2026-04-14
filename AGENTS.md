# ForgeGPU Workspace Agent Rules

This is the authoritative rule file for the ForgeGPU workspace.
It applies to everything under this folder unless a deeper `AGENTS.md` overrides it.

## 1) Workspace Purpose

ForgeGPU is a recruiter-facing AI inference orchestration demo built phase by phase in .NET 10.
The repository should demonstrate practical platform engineering patterns for:
- job submission
- queue-based execution
- worker processing
- scheduler-oriented evolution
- future multi-worker and GPU-awareness simulation

## 2) Repository Structure Expectations

Current and expected solution layout:
- `ForgeGPU.Api`: HTTP endpoints, host composition, DI wiring, logging, runtime configuration
- `ForgeGPU.Core`: domain models, enums, interfaces, orchestration abstractions
- `ForgeGPU.Infrastructure`: queue/store/worker implementations and runtime integrations

Keep module boundaries explicit so later phases can add scheduling, retries, batching, and metrics without major rewrites.

## 3) Architecture Boundaries (Mandatory)

- Keep domain behavior and orchestration contracts in `ForgeGPU.Core`.
- Keep `ForgeGPU.Api` thin; do not embed domain policy in controllers or host bootstrap.
- Keep implementation details in `ForgeGPU.Infrastructure` behind Core interfaces.
- Avoid leaking infrastructure concerns into API contracts.
- Prefer explicit domain naming (`InferenceJob`, `JobStatus`, `IJobQueue`, etc.) over generic names.

## 4) Development Standards

- Target framework is `net10.0` only.
- Use async-first patterns for I/O and background processing.
- Use structured logging for lifecycle and failure paths.
- Keep comments and docs in English.
- Avoid unnecessary abstractions; introduce indirection only when it clearly supports upcoming phases.
- Keep changes scoped to the requested phase.

## 5) Validation Expectations

After code changes, run relevant checks before handoff:
- `dotnet build ForgeGPU.sln`
- `dotnet test ForgeGPU.sln` when test projects exist

If tests do not exist yet, state that explicitly in the handoff.

## 6) Safety and Scope Rules

- Do not commit, push, or rewrite history unless explicitly requested.
- Do not add unrequested phases/features.
- Do not replace requested behavior with speculative architecture work.
- Keep fixes evidence-driven and minimal.

## 7) Documentation Expectations

- Keep `README.md` aligned with the current implemented phase.
- Document behavior or architecture changes that affect workflow, contracts, or operational understanding.
- Keep docs concise and implementation-accurate.

## 8) Handoff Output Rule

Final handoff must include:
- what changed
- validation commands run and outcomes
- known limitations or deferred items relevant to current phase

ForgeGPU Workspace Agent Rules

This file defines working rules for the `ForgeGPU` workspace root.
It applies to everything under this folder unless a deeper `AGENTS.md` overrides it.

---

## 1) Workspace Purpose

ForgeGPU is a recruiter-facing AI inference orchestration demo.

The goal is to incrementally build a production-minded system that demonstrates:
- job submission
- queue-based execution
- worker processing
- scheduler-oriented design
- GPU-awareness (later phases)
- batching and performance optimization (later phases)

This is not a CRUD application. All changes must align with inference orchestration concepts.

---

## 2) Repository Layout

- The workspace is a single .NET solution.
- Expected structure:

  - `ForgeGPU.Api` → HTTP layer, composition root
  - `ForgeGPU.Core` → domain models, enums, interfaces
  - `ForgeGPU.Infrastructure` → queue, store, worker implementations

- Do not introduce unrelated projects (frontend, UI, etc.) unless explicitly requested.

---

## 3) Architecture Boundaries (Mandatory)

- `ForgeGPU.Core`
  - Contains domain models (e.g., InferenceJob)
  - Contains contracts (IJobQueue, IJobStore)
  - Contains enums (JobStatus)
  - Must remain framework-agnostic

- `ForgeGPU.Infrastructure`
  - Implements queue, storage, worker, scheduling logic
  - Can depend on external systems (Redis, etc. in later phases)

- `ForgeGPU.Api`
  - Exposes HTTP endpoints
  - Handles request/response mapping
  - Wires dependencies via DI
  - Must remain thin (no business logic)

- Never move domain logic into controllers or hosting layer.

---

## 4) Backend Standards (.NET)

- Target framework: `net10.0`
- Do not downgrade framework version
- Use async/await for I/O-bound operations
- Prefer explicit domain naming over generic names
- Avoid overengineering abstractions unless they support upcoming phases
- Use structured logging

Validation commands:
- `dotnet build ForgeGPU.sln`
- `dotnet test` (when tests exist)

---

## 5) Development Workflow

- Build features as vertical slices
- Keep changes minimal and scoped
- Do not introduce future-phase features prematurely
- Always align naming and structure with "inference orchestration" domain

---

## 6) Job Lifecycle Contract

Inference jobs must follow a clear lifecycle:

- Queued
- Processing
- Completed
- Failed

Rules:
- State transitions must be explicit and traceable
- Timestamps must be recorded (Created, Started, Completed)
- Errors must be captured without crashing the worker loop

---

## 7) Scheduling & Queueing Direction (Future-Aware)

Even in early phases:
- Design queue and worker APIs so they can evolve into:
  - multi-worker execution
  - least-loaded scheduling
  - GPU-aware placement
  - batching

Do not hardcode assumptions that block future scheduling logic.

---

## 8) Logging & Observability

- Log key lifecycle events:
  - job submitted
  - job started
  - job completed
  - job failed

- Prefer structured logs over plain text
- Include job id in all relevant logs

---

## 9) Documentation

- All code comments must be in English
- README must reflect the current phase
- Document architectural decisions briefly when relevant
- Keep documentation aligned with actual behavior

---

## 10) Safety Rules

- Do not commit or push unless explicitly requested
- Do not rewrite git history unless explicitly requested
- Do not introduce unrelated features
- Do not silently change behavior contracts

---

## 11) Validation After Changes

- Always build after changes:
  - `dotnet build ForgeGPU.sln`

- If runtime behavior is modified:
  - verify API endpoints manually or via tests

- Do not claim completion without validation

---

## 12) Drift Prevention

- Do not change behavior implicitly
- If a change affects:
  - job lifecycle
  - scheduling behavior
  - API contract

then it must be explicitly stated

---

## 13) Output Guidelines

- Keep responses concise and technical
- Focus on architecture and correctness
- Avoid unnecessary verbosity
- Align all outputs with ForgeGPU’s purpose: inference orchestration demo