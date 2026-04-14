# ForgeGPU Agent Rules Overlay

This overlay maps practical workflow guidance to ForgeGPU work.
Root `AGENTS.md` remains authoritative if any conflict appears.

## 1) Bug Fix Flow

1. Reproduce with concrete request/log evidence.
2. Trace the flow: API -> queue -> worker -> store/result.
3. Isolate first failing boundary.
4. Implement the smallest safe fix.
5. Validate with `dotnet build ForgeGPU.sln` and tests when available.

## 2) Feature Flow

1. Clarify phase scope and acceptance behavior.
2. Implement an incremental vertical slice, not broad scaffolding.
3. Keep Core/Infrastructure/API boundaries intact.
4. Add or update tests when behavior meaningfully changes.
5. Update README if contracts or runtime behavior changed.

## 3) Validation Discipline

- Treat build/test failures as blockers unless explicitly marked unrelated.
- Report command results clearly.
- Distinguish pre-existing issues from introduced regressions.

## 4) Architecture Discipline

- Domain contracts and orchestration abstractions live in Core.
- Runtime mechanics (queue, worker, storage adapters) live in Infrastructure.
- API remains a thin orchestration surface.
- Avoid unnecessary abstractions in early phases.

## 5) Incremental Platform Mindset

Optimize for clean phase-by-phase evolution toward:
- multi-worker execution
- scheduler-driven dispatch
- GPU-capacity simulation
- dynamic batching
- observability and reliability controls
