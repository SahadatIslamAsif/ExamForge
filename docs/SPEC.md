# ExamForge — Technical Spec

Read `docs/PRD.md` first for the problem and the three properties that matter.

## Stack

Fixed. Chosen to match the role this project supports; do not substitute.

| Concern | Choice |
|---|---|
| Language | C# |
| Framework | ASP.NET Core Web API |
| ORM | Entity Framework Core |
| Database | PostgreSQL (Npgsql provider) |
| Tests | xUnit |
| API docs | Swagger / OpenAPI |
| Local DB | Docker Compose (Postgres only) |
| SDK | .NET 10 (installed: 10.0.402), pinned with `global.json` |

Target the installed SDK — confirm with `dotnet --version` before generating the
project, and do not hand-write `.csproj` package versions. Add packages with
`dotnet add package` so versions resolve against the real SDK. Create `global.json`
with `dotnet new globaljson` and `.gitignore` with `dotnet new gitignore`.

Which package serves the Swagger UI is a dependency decision: state the choice and
the reason before adding it.

## Layout

```
ExamForge.sln
global.json
.gitignore
docker-compose.yml             postgres
seed/questions.json            seed data (original questions only)
src/ExamForge.Api/          endpoints, DTOs, validation, DI, Program.cs
src/ExamForge.Domain/       entities, enums, domain rules, and the solver
    Generation/                PaperSolver, IRandomSource, SplitMix64Random
src/ExamForge.Infrastructure/  DbContext, EF configs, migrations, services, seeder
tests/ExamForge.Tests/      xUnit (unit tests for the solver, integration tests for the API)
```

`ARCHITECTURE.md` at the repository root describes the layout as built.

Controllers must stay thin: validate, call a service, map the result. No generation
logic in a controller.

**The solver is pure.** `PaperSolver` takes in-memory data (pools, targets, an
`IRandomSource`) and returns either a selection or a list of failures. It never touches
`DbContext`, the clock, or a global random. A service in Infrastructure loads the
pools and history from the database, calls the solver, and persists the result. This
keeps the hard logic unit-testable without a database and makes determinism easy to
prove.

## Data model

Five tables. `PaperQuestion` is the join carrying order.

**Subject** — `Id`, `Name`, `Level` (e.g. "O Level", "Class 9"), `LastGeneratedAt`
(timestamp, nullable), `Version` (concurrency token — see Concurrency)

**Chapter** — `Id`, `SubjectId` → Subject, `Name`, `OrderIndex`

**Question** — `Id`, `ChapterId` → Chapter, `Text`, `Marks` (int), `Difficulty`
(enum: Easy/Medium/Hard), `Type` (enum: Mcq/Short/Structured), `IsActive` (bool),
`CreatedAt`

**Paper** — `Id`, `SubjectId` → Subject, `Title`, `TotalMarks`, `Seed` (long, always
set), `SpecJson` (the validated specification, camelCase, with the seed actually used
filled in; a `json` column), `CreatedAt`

**PaperQuestion** — `Id`, `PaperId` → Paper, `QuestionId` → Question, `OrderIndex`

Constraints and indexes:
- `Question.Marks > 0` (check constraint).
- Unique `(PaperId, QuestionId)` and unique `(PaperId, OrderIndex)` on `PaperQuestion`.
- Unique `(SubjectId, OrderIndex)` on `Chapter`.
- Index `Paper (SubjectId, CreatedAt DESC, Id DESC)` — it serves the history query.

Notes:
- `SpecJson` stores the specification so a paper can be audited or re-run. It is the
  canonical serialisation of what was validated, not the raw HTTP body, and it always
  carries the seed used, including one the service chose.
- `Subject.Version` is an EF Core concurrency token, backed by Postgres's `xmin`
  system column (a `uint` property configured as a row version). Check the correct
  mapping for the installed Npgsql provider version before writing it; do not copy it
  from older examples. `LastGeneratedAt` exists so that every generation updates the
  `Subject` row, which is what makes the token fire (see Concurrency).
- `IsActive` soft-deletes a question without orphaning the papers that used it.
- A `Question` that appears in any `Paper` is frozen: it cannot be deleted, and only
  `IsActive` can change. Changing its `Text`, `Marks`, `Difficulty`, `Type` or
  `ChapterId` is a `409 QUESTION_IN_USE` (a field sent with its current value is not a
  change). Deactivate it and create a new one. This keeps stored papers truthful when
  they are read back later: a paper shows exactly what was set.

## Endpoints

CRUD, unremarkable:

```
POST   /api/subjects          GET /api/subjects          GET /api/subjects/{id}
POST   /api/chapters          GET /api/chapters?subjectId=   GET /api/chapters/{id}
POST   /api/questions         GET /api/questions?chapterId=&difficulty=
GET    /api/questions/{id}
PATCH  /api/questions/{id}    (including IsActive)
DELETE /api/questions/{id}    204 if never used in a paper, 409 if it was
```

As built:
- `POST /api/chapters` with an `OrderIndex` already used in the subject → `409
  CHAPTER_ORDER_TAKEN`.
- `PATCH` or `DELETE` on a question used in a paper → `409 QUESTION_IN_USE`, unless the
  `PATCH` changes only `isActive`.
- A `PATCH` that keeps colliding with generations for the question's subject → `409
  CONCURRENT_EDIT` after 3 attempts (see Concurrency).

**Content types.** The API accepts and returns `application/json` only (no `text/json`,
no `text/plain`). `400` and `404` are `application/problem+json`: `400` is a
`ValidationProblemDetails` whose `errors` are keyed by the JSON path of the field
(`title`, `chapterDistribution[1].marks`, `difficultyMix.easy`). `409` and `422` are
`application/json` bodies with an `error` code and a `message`. Enums travel as names
(`"Easy"`), never numbers.

The two that matter:

```
POST   /api/papers/generate
GET    /api/papers/{id}
GET    /api/papers?subjectId=
```

### POST /api/papers/generate

Request:

```jsonc
{
  "subjectId": 1,
  "title": "Physics Practice — Week 6",
  "totalMarks": 50,
  "chapterDistribution": [          // marks per chapter; must sum to totalMarks
    { "chapterId": 3, "marks": 20 },
    { "chapterId": 4, "marks": 20 },
    { "chapterId": 5, "marks": 10 }
  ],
  "difficultyMix": {                 // target share of the WHOLE paper, in marks; soft (A5, A7)
    "easy": 0.3, "medium": 0.5, "hard": 0.2
  },
  "excludeLastNPapers": 3,           // required; 0 disables
  "seed": 20261018                   // optional, within ±(2^53 − 1); omit for random
}
```

Success → `201` with the generated paper and its questions in order. The response
includes the `seed` actually used and the `achievedDifficultyMix`, so a miss on the
soft constraint is visible and not hidden:

```jsonc
{
  "id": 42,
  "subjectId": 1,
  "title": "Physics Practice — Week 6",
  "totalMarks": 50,
  "seed": 20261018,
  "achievedDifficultyMix": { "easy": 0.28, "medium": 0.52, "hard": 0.20 },
  "questions": [
    {
      "orderIndex": 1,
      "questionId": 17,
      "chapterId": 3,
      "chapterName": "Forces and Motion",
      "text": "…",
      "marks": 2,
      "difficulty": "Easy"
    }
  ],
  "createdAt": "2026-10-18T10:00:00Z"
}
```

Failure → `422 Unprocessable Entity` with a body naming every failed constraint.
Two constraint types exist.

`chapterDistribution` — the eligible pool has fewer marks than requested:

```jsonc
{
  "error": "INFEASIBLE_SPECIFICATION",
  "message": "Cannot satisfy the requested mark distribution.",
  "failures": [
    {
      "constraint": "chapterDistribution",
      "chapterId": 3,
      "chapterName": "Forces and Motion",
      "requestedMarks": 20,
      "availableMarks": 12,
      "detail": "8 marks short after excluding questions used in the last 3 papers."
    }
  ]
}
```

The `detail` says why: "N marks short after excluding questions used in the last 3
papers." when the history window removed questions from that chapter, otherwise "N
marks short: the chapter's active questions total M marks." Singulars are written as
such ("1 mark", "the last paper").

`exactMarks` — the pool has enough marks in total, but no combination sums to exactly
the target:

```jsonc
{
  "error": "INFEASIBLE_SPECIFICATION",
  "message": "Cannot satisfy the requested mark distribution.",
  "failures": [
    {
      "constraint": "exactMarks",
      "chapterId": 4,
      "chapterName": "Energy",
      "requestedMarks": 9,
      "availableMarks": 12,
      "detail": "Eligible questions total 12 marks, but no combination sums to exactly 9."
    }
  ]
}
```

Validation (`400`, before any solving):
- `subjectId` present.
- `title` present and non-empty.
- `totalMarks` positive; distribution non-empty and sums to `totalMarks`; every
  entry's marks positive.
- No `chapterId` appears twice in the distribution.
- Every chapter belongs to the subject.
- Difficulty shares are each present, between 0 and 1, and sum to 1.0 (tolerance 1e-6).
- `excludeLastNPapers` **present** and 0 or greater. There is no default: a client that
  leaves it out must not silently lose the history window.
- `seed`, when present, within ±(2^53 − 1), JavaScript's `Number.MAX_SAFE_INTEGER`.
  JavaScript reads JSON numbers as doubles, so a larger seed could be rounded in
  transit and no longer reproduce its paper.

All of these except chapter ownership are checked in the API before any database
access. Ownership needs the database, so the generation service reports it (as a
`chapterDistribution` field error listing the foreign chapter ids), after the subject
lookup and before loading history or solving.

An unknown `subjectId` → `404`. It is checked before chapter ownership.

Concurrency: if the generation still conflicts with another request for the same
subject after 3 attempts (3 attempts in total, not 3 retries) → `409` with
`{ "error": "CONCURRENT_GENERATION", "message": "…" }`.
This is not a `422`: the specification is fine, the server was busy.

## Generation algorithm

Chapters are processed independently for the hard constraints — each chapter's mark
target is exact and chapters do not compete. Only the soft difficulty mix looks across
chapters (step 3).

1. **Pool.** For each chapter: active questions, minus any question used in the last
   `excludeLastNPapers` papers for this subject. "Last N" means the N most recent
   papers for the subject, ordered `CreatedAt DESC, Id DESC`. Order the pool by
   question `Id` so the solver's input is stable.
2. **Feasibility.** For every chapter, check two things: pool marks ≥ requested marks
   (else a `chapterDistribution` failure), and the requested marks are reachable as a
   subset sum of the pool (else an `exactMarks` failure). Use a subset-sum DP over the
   pool's marks — it answers exactly, with no search cap. Check every chapter before
   failing so the error reports all shortfalls, not just the first.
3. **Select.** Per chapter, in chapter `OrderIndex` order: shuffle the pool with the
   shared seeded random source, then walk it depth-first (take or skip each question),
   using the DP table to prune any branch whose remaining marks cannot be reached, so
   the search never dead-ends. Collect up to 64 exact-sum combinations, then pick the
   one that brings the paper's *cumulative* difficulty mix (marks-weighted, across the
   chapters chosen so far plus this one) closest to the requested mix (smallest L1
   distance). Ties go to the first combination found. Exact marks per chapter are
   mandatory; the mix is only a preference.
4. **Order.** Sort by chapter `OrderIndex`, then difficulty ascending (Easy, Medium,
   Hard), then question `Id`, so papers open with easier questions and the order is
   a total order with no ties.
5. **Persist.** Save `Paper` and `PaperQuestion` rows in one transaction, with the
   request in `SpecJson` and the seed used. In the same transaction, set
   `Subject.LastGeneratedAt`; the `Subject` concurrency token is what rejects a commit
   made on stale history (see Concurrency).

Before returning success, assert in code that the selected marks equal `totalMarks` and
each chapter's marks equal its target. If the assertion fails, that is a bug: return a
`500`, never a short paper.

### Determinism

**What is guaranteed.** The same specification and seed, run against the same question
bank and the same paper history, produce an identical paper: same questions, same order.
Adding or deactivating questions, or generating other papers in between, legitimately
changes the result. The stored `Paper` and `PaperQuestion` rows are the permanent
record of a paper; the seed and `SpecJson` make it auditable and re-runnable on the same
data.

**Random source.** All randomness flows through one `IRandomSource`, created once per
generation from the request seed. Do not use `System.Random` in the selection path:
its seeded sequence is not guaranteed to stay the same across .NET versions, and it takes
an `int` while the seed is a `long`. Implement SplitMix64 (about ten lines):

```
state = seed
next():
  state += 0x9E3779B97F4A7C15
  z = state
  z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
  z = (z ^ (z >> 27)) * 0x94D049BB133111EB
  return z ^ (z >> 31)
```

All on unsigned 64-bit values with wrapping arithmetic. `NextInt(n)` may use
`next() % n` (the modulo bias is negligible at these sizes; document it). Shuffle with
Fisher–Yates from the end of the list.

**When no seed is supplied.** The generation service picks one, *before* the solver
runs and before the first attempt, from an injected `ISeedSource`
(`Random.Shared.NextInt64(0, 2^53)`, so 0 to 2^53 − 1, the same safe range the API
accepts). That is the only place non-seeded randomness is allowed, and it sits in the
service rather than the controller so every caller of the service gets it and tests can
substitute the source. Every retry reuses the same seed. **Store it on the paper**
(`Seed` and `SpecJson`) and return it, so the paper can be reproduced after the fact.

Nothing else may introduce nondeterminism. In particular, every query feeding
selection needs an explicit `ORDER BY`: Postgres row order is not stable, and an
unordered query will break reproducibility in a way that is miserable to debug.

### Concurrency

History-aware selection must hold when requests overlap. Without protection, two
simultaneous requests for the same subject both read the same history, both see the
same questions as "unused", and may both pick them, so the exclusion rule silently
breaks.

Use optimistic concurrency on the `Subject` row:

1. Load the `Subject` (with its `Version`), then load the history and pools, then solve.
2. In one transaction, insert the `Paper` and `PaperQuestion` rows and update
   `Subject.LastGeneratedAt`.
3. If `SaveChanges` throws `DbUpdateConcurrencyException`, another generation for this
   subject committed first. Discard everything, reload from step 1 (the new paper is
   now part of the history) and retry. Make at most 3 attempts, then return `409`.

Scope and consequences:
- Per subject. Generations for different subjects must not block or conflict with
  each other.
- A retry re-runs the solver against fresher history, with the same seed. Under
  concurrency, which request "goes first" is not deterministic, and that is
  acceptable: the guarantee is that no stored paper ignores an earlier committed paper
  that falls inside its exclusion window.
- Do not hold database locks while solving; the conflict is detected at commit.
- Do not catch and swallow the exception: a conflict that is not retried or reported
  is a silent history violation.
- Updating `LastGeneratedAt` is forced (`IsModified = true`), so the `UPDATE`, and with
  it the version check, happens even if the timestamp happens to be unchanged.

**Question edits take part.** Without this, a `PATCH` could pass its "used in a paper?"
check, a generation could then commit a paper containing that question, and the edit
would rewrite a stored paper. So a `PATCH` that changes anything but `isActive` loads
the question's subject (both subjects, on a chapter move) tracked, *before* the usage
check, and writes the row back unchanged in the same `SaveChanges` as the edit.
Postgres gives a row a new `xmin` on every `UPDATE`, even an identical one, so either
the usage check sees the paper, or the edit's save (or the generation's) fails and
retries with fresh data. After 3 attempts the edit returns `409 CONCURRENT_EDIT`.
Changing only `isActive` skips this: it never alters what a stored paper shows.

## Tests

The suite exists to prove the three properties. Write these first; they are the
project's argument. Solver tests are plain unit tests with in-memory data; API tests
are integration tests.

**Determinism**
- PRNG golden test: seed 42 produces a fixed, hard-coded sequence of values, so any change to the generator fails loudly.
- Same spec + same seed → identical question IDs in identical order.
- Same spec, different seeds → differing selections (given an ample pool).
- Omitted seed → seed is persisted and returned, and reusing it reproduces the paper.
- Note for integration tests: generating a paper adds to the history. Use
  `excludeLastNPapers: 0`, or restore the database between the two runs, when
  comparing two generations.

**Honest failure**
- Chapter short on marks → `422`, names that chapter, reports requested vs available.
- Two chapters short → both appear in `failures`.
- Exclusion window empties a pool → `422`, not a short paper.
- Pool has enough marks but no exact combination (e.g. only 2-, 4- and 6-mark
  questions, target 9) → `422` with constraint `exactMarks`.
- **Never returns a paper whose marks differ from `totalMarks`.** Assert this
  across many randomised specs (a property-style test that runs hundreds of random
  specs against the solver).

**History-aware selection**
- Question used in the last 3 papers is absent when `excludeLastNPapers: 3`.
- The same question is eligible again at `excludeLastNPapers: 2` once it has aged out.
- `excludeLastNPapers: 0` disables exclusion.

**Validation**
- Distribution not summing to total → `400`.
- Chapter from another subject → `400`.
- Duplicate chapter in the distribution → `400`.
- Missing title → `400`.
- Unknown subject → `404`.

**Concurrency**
- Two generate requests for the same subject fired in parallel with
  `excludeLastNPapers: 1` and a pool large enough for two disjoint papers → both
  succeed (`201`) and the two papers share no question. Repeat the race many times
  (e.g. 20 rounds) so a one-in-a-hundred interleaving cannot hide.
- Requests for two different subjects in parallel → both succeed with no `409`.
- A request that conflicts on every attempt → `409 CONCURRENT_GENERATION`, and no paper
  is stored for it.
- Deliberately disable the concurrency token once and confirm the parallel test fails,
  so we know the test can actually detect the bug.

**Question integrity**
- Deleting or re-marking a question used in a paper → `409`; deleting an unused one → `204`.

**Correctness**
- Generated marks equal `totalMarks` exactly.
- Per-chapter marks match the distribution exactly.
- `achievedDifficultyMix` in the response matches the questions actually returned.
- Difficulty mix lands within ±0.10 per level when the pool allows it.

Use a real Postgres for integration tests (Testcontainers, or the Compose instance).
The InMemory provider does not behave like Postgres and will hide ordering bugs.
Testcontainers needs Docker running.

**As built**, beyond the list above (numbers and the mutation results are in
`ARCHITECTURE.md`):
- Property tests over HTTP on the real seed bank: 300 random requests, every `201`
  checked for exact marks, ordering and history, every `422` checked against a
  brute-force subset-sum oracle; 20 papers replayed from their seed; 50 parallel
  requests ending only in `201` or `409`.
- `PATCH` racing a generation, in both orders, and the `CONCURRENT_EDIT` limit.
- What the seed bank can and cannot sustain (see Seed data).

## Seed data

A seeding command with enough real O-level physics or chemistry questions to make
generation meaningful — roughly 60–80 across 5–6 chapters, about 12 per chapter.

- **Original questions only.** Do not copy from Cambridge past papers or published
  textbooks. Chapter names may follow the public syllabus; question text must be original.
- **Spread of marks.** Include plenty of 1-, 2- and 3-mark questions in every chapter,
  not only large ones, so that exact targets are usually reachable.
- **Spread of difficulty** in every chapter.
- Stored in `seed/questions.json`. The command is
  `dotnet run --project src/ExamForge.Api -- --seed`, and it is idempotent (it does
  nothing if the bank is not empty). It applies pending migrations first, so a fresh
  database needs no separate `dotnet ef database update`.

**As built:** 72 questions, O Level Physics, 6 chapters (Motion, Forces, Energy and
Pressure, Thermal Physics, Waves and Light, Electricity). Every chapter has 12
questions worth 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4 and 5 marks (29 in all) and 4 Easy,
5 Medium and 3 Hard questions.

**Capacity.** With `excludeLastNPapers = N`, every N + 1 consecutive papers must use
different questions, so in a steady run a chapter can supply about 29 / (N + 1) marks
per paper. At N = 3 that is about 7: six 30-mark papers at 5 marks per chapter all
succeed, while 15 marks from a chapter succeeds once and then fails honestly (14
eligible marks left). Both are pinned by tests. A real bank for weekly papers with a
long window needs more questions per chapter, not a cleverer solver.

The local connection string for Development is a placeholder in
`appsettings.Development.json` that matches `.env.example`, so a fresh clone runs
without setup. User secrets and the `ConnectionStrings__ExamForge` environment
variable override it; real passwords go only there.

A reviewer cloning the repo should be able to generate a paper within a minute of
starting the containers.

## README

Written last, but it is what a reviewer reads first. It must cover: what the project
does and why, how to run it (`docker compose up`, seed, which also migrates, run), one example
generate request with its response, one example failure response, how to run the
tests, and a short section on the design decisions above (determinism, honest failure,
history-awareness, concurrency) and the reasoning behind each. Add one line stating that there is deliberately no LLM in the generation
path, because exactness and reproducibility are the requirements.
