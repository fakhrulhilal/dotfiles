---
name: clean-code
description: Practical rules for writing and cleaning code that the next developer can read and change safely - intent-revealing names, short functions with few arguments and no boolean flags, early returns over nesting, explicit errors and absence, no duplicated rules, and almost no comments (no doc comments on private or internal code, ever; a summary of what a method does means the name is wrong). Includes a diff pass to run before finishing. Use when writing, reviewing, refactoring or tidying code in any language, when asked to "clean up", "apply clean code", simplify or make code readable, when naming things or splitting functions or classes, when deciding whether a comment is needed, or when writing the commit message for such a change (business-first subject of at most 8 words). Repository conventions and language skills win where they differ.
---

# Clean code

The goal is one thing: the next person changes this code quickly and without breaking it. Every rule below is a
concrete way to get there. When a rule doesn't serve that goal in the case at hand, skip it and say why.

The repository's conventions win: `AGENTS.md`, `.editorconfig`, the formatter and the analyzers it enforces. Match the
code around you before applying anything here.

## Comments: none, unless it is a "why" the code can't hold

This is the rule agents break most, so it comes first.

- **No doc comments on private or internal members.** Not a summary, not `<param>`, not `<returns>`. If the method
  needs a sentence to say what it does, rename it or split it until it doesn't.
- **A summary of what the code does is never a reason to comment.** It is either already obvious from the name and
  body, or the name is wrong.
- Doc comments on public members only when a tool publishes them (a library's API docs, OpenAPI, CLI help) or the
  repository requires them. An analyzer rule that demands docs everywhere (e.g. StyleCop SA1600) is not a reason to
  write filler: if it is enforced, tell the user instead of padding the code.
- Allowed, as one short line: *why* a non-obvious choice was made, where a reader would otherwise "fix" it (a
  workaround with a link, a constraint of an external system, a deliberate trade-off), or a warning (not thread-safe,
  order matters, slow).
- Never: step narration (`// validate input`), banners or `#region`, notes about the change (`// fixed`, `// new`),
  commented-out code, untracked TODOs. Change notes go in the commit message; design rationale goes in `docs/*.md`.

```csharp
// Bad: restates the method, adds lines, rots when the body changes.
/// <summary>Stores the platform ids of the migrated user and, when known, of its organization.</summary>
private async Task LinkToPlatformAsync(ClientUser clientUser, MigratedUser migrated, CancellationToken ct)

// Good: the name and the two statements inside already say it.
private async Task LinkToPlatformAsync(ClientUser clientUser, MigratedUser migrated, CancellationToken ct)

// Good: a why the code can't express.
// Recorded rather than rethrown: the row stays retryable and the partition keeps moving.
```

Before finishing, scan the diff for every comment you added and delete each one that doesn't pass this section.

## Names

- Say what it holds or does, in the domain's words: `gracePeriodUntil`, not `date2`; `organizationHubexoId`, not
  `orgHid`. No abbreviations a newcomer would have to guess.
- Functions are verbs (`StartRiseRunAsync`), types and variables are nouns. Booleans read as questions (`isSuspended`,
  `hasActiveSubscription`).
- One word per concept in a module: don't mix `Get`, `Fetch` and `Load` for the same thing. When two do differ, make
  the difference real (`GetCountryAsync` reads the cache, `FetchCountryAsync` calls the service).
- Parallel things get parallel names (`organizationReasons` and `userReasons`, `ReachAccountStageAsync` and
  `ReachUgcStageAsync`).
- If you renamed something, rename it everywhere, including tests and log messages.

## Functions

Extract a function when:

- you'd otherwise label a block with a comment: the label becomes the name;
- a method mixes levels, e.g. business decisions next to SQL, HTTP or JSON details;
- a `try` block holds more than the few calls that can actually fail;
- nesting goes past two levels: prefer early returns and guard clauses;
- the same rule appears twice: one helper, one place to change it.

Don't extract when the helper is a one-line rename of a single call used once, or when the reader would have to jump
around more than before. Fewer, well-named functions beat many tiny ones.

Arguments:

- Two is normal, three needs a reason, more means a missing type: pass the object, not five of its fields.
- Don't pass what the callee can read from an argument it already has (`ValidateAsync(clientUser)` instead of
  `ValidateAsync(clientUser, clientUser.ClientId)`).
- Boolean or `null` literals at a call site get a named argument (`GetAsync(id, includeSubscription: false)`). If a
  boolean switches between two behaviors you own, split it into two functions.
- A function either changes something or answers a question. If it must do both, its name says so.

## Errors and absence

- Failure is explicit: an exception or a typed result per the language and repository. No magic `-1`, `""` or `null`
  for "it failed".
- Make absence visible in the type (nullable annotations, `Option`). Return empty collections, not `null`.
- Error and log messages say what failed, for which input, and what happens next
  (`"Client {ClientId} not found, so it cannot reach the UGC stage"`, not `"About to set stage but not found"`).
- Catch only what you handle, as narrowly as possible, and keep the happy path out of the catch logic.

## Duplication and dead code

- Same knowledge in two places is a bug waiting to happen: a rule, a format, a magic string or number. Give it one
  home. Code that merely looks alike but would change for different reasons stays separate.
- Delete unused code, parameters, usings and stale config. Version control remembers.
- Replace magic values with named constants or enums when the meaning isn't obvious at the use site.

## Layout

- Follow the analyzer's member order (e.g. public before private). Within it, put each private helper below the
  method that uses it, in call order, so the file reads top-down.
- Keep lines short enough to read without scrolling; wrap long calls one argument per line, as the codebase does.
- Declare variables next to their first use.

## Classes and boundaries

- A class you can't name without "and", `Manager`, `Helper` or `Processor` does too much. Say so; split it only when
  asked or when the change you are making needs it.
- Talk to your direct collaborators, not through them (`order.Customer.Address.City` in business logic means the
  logic belongs closer to the data).
- Keep third-party and external-service types at the edge, behind a small interface shaped by what your code needs.

## Tests

- Each test name states the behavior (`FailsWhenOrganizationHasNoActiveSubscription`), with arrange, act and assert
  visible at a glance. Hide noisy setup in helpers named after intent.
- One behavior per test. Tests don't depend on each other, the clock, the network or run order.
- Fixing a bug starts with a test that fails because of it.

## How to do a cleanup

1. Find what protects the code: the tests that cover it. If none do and the change is risky, add one first.
2. Change behavior or structure, never both in one step. A cleanup preserves behavior exactly; if you spot a bug, report
   it rather than fixing it silently.
3. Stay in proportion: clean what the task touches. Larger refactors (splitting a class, changing an interface) are
   proposals, not side effects.
4. Build and run the tests after each meaningful step. Check the analyzer output didn't get worse.
5. Report briefly: what changed and why it reads better, what you verified, and what you deliberately left.

## Commit message

The repository's own convention wins; otherwise follow the shape of its recent history.

- Subject: `TICKET-123: <kind of change> <business feature or flow>`, at most 8 words after the ticket. Name the
  feature the way a product owner would, never the code: "Bug fix creating user from service account",
  "Update migration policy", "Add dry-run option", "Clean up user migration".
- This holds for cleanups and refactors too: the subject names the feature touched ("Clean up user migration"), not
  "tidy code", "refactor service" or "behaviour unchanged".
- Body: `- ` bullets, wrapped at 72 characters, one effect per bullet, visible effects first. Technical wording only
  when the business wording can't carry it. No lists of files or methods.

```text
SMDN-1334: Clean up user migration

- Refusal message for organizations without a Hubexo or official ID
  now ends with a full stop, like the other eligibility reasons
- Clearer warning when a client is missing at the UGC stage
- Migration steps reorganised so each reads in order and can be
  changed on its own; no change to who is migrated or how
```

## Diff pass before finishing

- [ ] Every comment and doc comment I added passes the comments section. None on private or internal members.
- [ ] Names say what things are; no abbreviations, no mixed vocabulary.
- [ ] No method mixes levels or needs a comment to label its sections; nesting is shallow.
- [ ] No boolean or null literal passed without a name; no argument the callee could derive itself.
- [ ] No duplicated rule, magic value or dead code introduced.
- [ ] Errors and log messages say what failed, for what, and what happens next.
- [ ] Behavior unchanged (for a cleanup); tests and build pass; no new analyzer warnings.

More before/after cases: [references/examples.md](references/examples.md). A catalog of smells, for looking up a
specific one: [references/smells.md](references/smells.md).
