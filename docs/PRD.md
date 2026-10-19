# ExamForge — Product Requirements

> Working name. Rename freely; it follows the naming pattern of my other projects
> (BartaScope). `PaperForge` or `PaperBank` are fine alternatives.

## Problem

I tutor six English-medium students across grades 5–10 at O Level standard. Setting
practice papers by hand is slow and error-prone in three specific ways:

1. **Mark totals drift.** Assembling a 50-mark paper by hand means adding marks in
   your head. Papers come out at 47 or 53 and need reworking.
2. **Coverage is uneven.** Without tracking it, the same two or three chapters
   dominate paper after paper, and weak chapters go untested.
3. **Questions repeat too soon.** A question used three weeks ago reappears, and
   the student recognises it rather than solving it, so the paper stops measuring
   anything.

Doing this for several subjects and several students multiplies the problem.

## What this is

A REST API that holds a bank of exam questions and generates practice papers to a
requested specification: total marks, how those marks are distributed across
chapters, a difficulty mix, and an exclusion window so recently used questions are
not reused.

## Who it is for

Me, and tutors like me. There is no multi-tenant model in v1 — one bank, one user.

## In scope (v1)

- Manage subjects, chapters, and questions (CRUD).
- Generate a paper from a specification.
- Store every generated paper so history drives future exclusions.
- Retrieve a generated paper.

## Out of scope (v1)

Deliberately excluded. These cost time and demonstrate nothing the generation logic
does not already demonstrate:

- Authentication and user accounts
- A frontend of any kind
- PDF or DOCX export
- Multi-tenancy
- Question images, diagrams, or rich text
- Marking, grading, or student results
- An admin panel
- Any LLM or AI call in the generation path (exactness and reproducibility are the
  requirements)

In v1 the API is used through the Swagger page or plain HTTP requests.

## Later (not v1)

Considered and deferred. None of this starts until v1's generator and its tests pass,
and each needs an explicit instruction to begin:

- A small Next.js frontend in a separate `web/` folder (needs CORS on the API).
- PDF or DOCX export of a generated paper.
- LLM-assisted drafting of candidate questions for the bank, reviewed by a human
  before they enter it — never inside generation.

## The three properties that matter

This project exists to show backend judgement, not CRUD. Three behaviours carry
that, and they are requirements, not nice-to-haves.

### 1. Deterministic generation

A generation request carries an optional seed. The same specification and the same
seed, run against the same question bank and the same paper history, must produce an
identical paper — same questions, same order. Without a seed, the service picks one
before solving, and that seed is stored and returned. Seeds stay within JavaScript's
safe integer range (±(2^53 − 1)), so a browser or Node client can hold one without
rounding it into a different paper.

Why it matters: reproducibility. If a student reports a problem with a paper, I can
inspect exactly that paper, and re-run its seed and specification to audit it.

What this does not promise: adding questions or generating other papers in between
changes the bank and the history, so the same seed can legitimately give a different
paper later. The stored paper itself never changes.

### 2. Honest failure

If the bank cannot satisfy the specification, the API returns an error naming the
constraint that could not be met and what was available.

It must **never** return a paper that quietly falls short of the requested marks or
chapter distribution. Partial success presented as success is the failure mode this
project is built to avoid.

Example: asked for 40 marks from Chapter 3, but only 25 marks of eligible questions
exist there after exclusions → fail, and say so, naming Chapter 3, the 40 requested,
and the 25 available.

A subtler case counts too: the pool has enough marks in total, but no combination
adds up to exactly the target (a chapter with only 2-, 4- and 6-mark questions cannot
make 9). That also fails, with its own constraint name.

### 3. History-aware selection

A specification may exclude any question used in the last N generated papers for
that subject. This requires querying prior papers at generation time. N is required in
every request (`0` turns the window off), so forgetting it is an error rather than a
silent "no exclusion".

This must hold when requests overlap. If two generation requests for the same subject
run at the same time, they must not both ignore each other: the second one either sees
the first one's paper in its history or is rejected with a clear error. It must never
store a paper that silently breaks its own exclusion window.

The same holds for editing a question: an edit must never land in a question at the
moment a paper starts using it.

### Stored papers stay true

A question that has appeared in a paper is frozen: it cannot be deleted, and only its
active flag can change. Retiring a question is a soft delete (deactivate it, write a
new one), so a paper read back months later shows exactly what the student was given.

## Success criteria

- Generating a 50-mark paper across 4 chapters takes one API call.
- Mark totals are exact, every time.
- An impossible request fails with a message explaining why, every time.
- The same seed, bank and history reproduce the same paper, every time.
- Two simultaneous requests for one subject never produce papers that violate each
  other's exclusion window.
- The behaviours above are covered by tests that would fail if the behaviour broke.

## Assumptions

Each of these was decided rather than asked. Change any of them and the schema or
the solver changes with it.

| # | Assumption | If wrong |
|---|-----------|----------|
| A1 | A question has one fixed mark value. The same question is always worth the same. | `Marks` moves from `Question` onto `PaperQuestion`, and the solver gets harder. |
| A2 | Difficulty is a three-level enum: Easy, Medium, Hard. | Change the enum; the mix constraint logic is unaffected. |
| A3 | Chapter distribution is specified in marks, not question counts. | Solver target changes from marks to counts. |
| A4 | A chapter belongs to exactly one subject. | Chapter–subject becomes many-to-many. |
| A5 | The difficulty mix is a target with tolerance, not a hard constraint. Marks and chapters are hard. | Generation fails far more often; decide the precedence order explicitly. |
| A6 | Single user, no auth. | Everything gains a tenant or owner column. |
| A7 | The difficulty mix applies to the whole paper, measured in marks (not question counts); tolerance is ±0.10 per level. | The solver balances per chapter instead, and the response reports per-chapter mixes. |
| A8 | Reproducibility is relative to the current bank and history (see property 1). | Snapshot the pool and exclusion set on each `Paper` so any paper can be replayed exactly. |
| A9 | Concurrency is handled per subject; generations for different subjects are independent. | The conflict scope widens (e.g. per tenant), and unrelated requests start retrying each other. |
| A10 | A question used in any paper is frozen except `IsActive`. | Papers must snapshot question text and marks at generation time. |
| A11 | Seeds are limited to ±(2^53 − 1) so JavaScript clients keep them exact. | Seeds travel as strings instead, and the API contract changes. |
| A12 | Three attempts per generation is enough; bursts of identical requests on one subject may get `409`. | Add a queue per subject, or retry with backoff. |

## As built: what the seed bank can sustain

The seed bank has 6 chapters of 29 marks each. Excluding the last N papers means every
N + 1 consecutive papers need different questions, so a chapter supplies about
29 / (N + 1) marks per paper in a steady run: about 7 marks at N = 3. Weekly papers
with a long window need a larger bank; the API says so with a `422` rather than
returning a weaker paper.

## Non-goals for the deadline

This is a portfolio project built to a short deadline. Where scope and quality
conflict, cut scope. A small API with excellent generation logic and real tests
beats a large one with shallow coverage.
