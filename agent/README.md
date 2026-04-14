# ForgeGPU Local Agent Workspace

This folder contains a curated, project-local subset of agent skills for ForgeGPU.

## Authority

`/Users/gokay/Documents/Workspace/ForgeGPU/AGENTS.md` is the primary authority.
This `agent/` workspace extends execution guidance and does not override root rules.

## What Was Selected

Selected skills support ForgeGPU phase-by-phase delivery needs:

- Planning and incremental implementation
- Debugging and recovery
- .NET API/interface and contract discipline
- Regression prevention (tests + review + CI checks)
- Security and performance hardening
- Architecture and decision documentation

## Included Skills

- `planning-and-task-breakdown`
- `incremental-implementation`
- `test-driven-development`
- `api-and-interface-design`
- `browser-testing-with-devtools`
- `debugging-and-error-recovery`
- `code-review-and-quality`
- `security-and-hardening`
- `ci-cd-and-automation`
- `documentation-and-adrs`
- `performance-optimization`

## Included References

- `references/testing-patterns.md`
- `references/security-checklist.md`
- `references/performance-checklist.md`
- `references/accessibility-checklist.md`

## How To Use In ForgeGPU

1. Start from root `AGENTS.md` constraints.
2. Pick one or two skills that match the current task phase.
3. Validate backend changes with `dotnet build ForgeGPU.sln` and tests when present.
4. Use personas in `agent/agents/` for role-focused reviews before handoff.

## Scope Guardrails

- Do not break Core/Infrastructure/API boundaries.
- Do not bypass validation failures silently.
- Do not add unrequested phase features.
- Keep patches scoped and reversible.
