# ForgeGPU Workspace Agent Rules

This is the authoritative rule file for the ForgeGPU workspace.
It applies to everything under this folder unless a deeper `AGENTS.md` overrides it.

## 1) Workspace Purpose

ForgeGPU is a recruiter-facing AI inference orchestration demo built phase by phase in .NET 10.
All implementation and guidance should support this direction:
- job submission
- queue-based execution
- worker processing
- scheduler-oriented evolution
- future multi-worker and GPU-awareness simulation

## 2) Repository Structure Expectations

Current and expected solution layout:
- `ForgeGPU.Api`: HTTP endpoints, host composition, DI wiring, logging, runtime configuration
- `ForgeGPU.Core`: domain models, enums, interfaces, orchestration abstractions
- `ForgeGPU.Infrastructure`: queue/store/worker/runtime implementations

Keep boundaries explicit so later phases can add scheduling, retries, batching, and metrics without major rewrites.

## 3) Architecture Boundaries (Mandatory)

- Keep domain behavior and orchestration contracts in `ForgeGPU.Core`.
- Keep `ForgeGPU.Api` thin; do not embed domain policy in controllers or host bootstrap.
- Keep runtime/integration details in `ForgeGPU.Infrastructure` behind Core interfaces.
- Avoid leaking infrastructure concerns into public API contracts.
- Prefer explicit domain naming (`InferenceJob`, `JobStatus`, `IJobQueue`, `IJobStore`, etc.).

## 4) Development Standards

- Target framework is `net10.0` only.
- Use async-first patterns for I/O and background processing.
- Use structured logging for lifecycle and failure paths.
- Keep comments and docs in English.
- Avoid unnecessary abstractions unless they clearly support upcoming phases.
- Keep changes scoped to the requested phase.

## 5) Validation Expectations

After code changes, run relevant checks before handoff:
- `dotnet build ForgeGPU.sln`
- `dotnet test ForgeGPU.sln` when test projects exist

If tests do not exist yet, state that explicitly in handoff.

## 6) Safety and Scope Rules

- Do not commit, push, or rewrite history unless explicitly requested.
- Do not add unrequested phases/features.
- Do not replace requested behavior with speculative architecture work.
- Keep fixes evidence-driven and minimal.

## 7) Documentation Expectations

- Keep `README.md` aligned with the currently implemented phase.
- Document behavior or architecture changes that affect workflow/contracts.
- Keep documentation concise and implementation-accurate.

## 8) Agent Workspace References (Mandatory)

When solving tasks in this repository, consult `agent/` guidance deliberately.

### 8.1 Primary overlay and personas
- `agent/README.md`
- `agent/forgegpu-agent-rules.md`
- `agent/agents/forgegpu-api-guardian.md`
- `agent/agents/forgegpu-debugger.md`
- `agent/agents/forgegpu-ui-guardian.md`

### 8.2 Skill references
- `agent/skills/api-and-interface-design/SKILL.md`
- `agent/skills/browser-testing-with-devtools/SKILL.md`
- `agent/skills/ci-cd-and-automation/SKILL.md`
- `agent/skills/code-review-and-quality/SKILL.md`
- `agent/skills/debugging-and-error-recovery/SKILL.md`
- `agent/skills/documentation-and-adrs/SKILL.md`
- `agent/skills/frontend-ui-engineering/SKILL.md`
- `agent/skills/incremental-implementation/SKILL.md`
- `agent/skills/performance-optimization/SKILL.md`
- `agent/skills/planning-and-task-breakdown/SKILL.md`
- `agent/skills/security-and-hardening/SKILL.md`
- `agent/skills/test-driven-development/SKILL.md`

### 8.3 Supporting checklists/references
- `agent/references/accessibility-checklist.md`
- `agent/references/performance-checklist.md`
- `agent/references/security-checklist.md`
- `agent/references/testing-patterns.md`

## 9) Agent Usage Rule

- Use root `AGENTS.md` as policy baseline.
- Use `agent/forgegpu-agent-rules.md` as execution overlay.
- Use persona files under `agent/agents/` when role-specific review is needed.
- Pull in skills/checklists from `agent/skills/` and `agent/references/` based on task type.
- If any guidance conflicts, root `AGENTS.md` takes precedence.

## 10) Handoff Output Rule

Final handoff must include:
- what changed
- validation commands run and outcomes
- known limitations or deferred items relevant to current phase
