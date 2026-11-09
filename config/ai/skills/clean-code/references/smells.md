# Smells and heuristics

The catalog from chapter 17 of *Clean Code*, paraphrased and generalized beyond Java. Codes match the book, so a
review can say "F3 flag argument" or "G36 transitive navigation". Each entry gives the smell and the usual fix.

## Comments (C)

| Code | Smell                     | What to do                                                                                  |
|------|---------------------------|---------------------------------------------------------------------------------------------|
| C1   | Inappropriate information | Change history, authors, ticket logs and metadata belong in version control or the tracker. |
| C2   | Obsolete comment          | A comment that no longer matches the code misleads. Update it or delete it.                 |
| C3   | Redundant comment         | It says what the code already says (`i++ // increment i`). Delete it.                       |
| C4   | Poorly written comment    | If a comment is worth writing, write it well: brief, correct grammar, no rambling.          |
| C5   | Commented-out code        | Delete it. Version control remembers it.                                                    |

### Comment smells common in generated code

Not in the book's catalog, but each is C3 (redundant) or C1 (inappropriate) in practice. Fix them with the comment
ladder in [SKILL.md](../SKILL.md): rename, extract, encode in a type, test, markdown doc, commit message.

| Smell                    | Example                                                 | What to do                                               |
|--------------------------|---------------------------------------------------------|----------------------------------------------------------|
| Signature-restating doc  | `/// <summary>Gets the order.</summary>` on `GetOrder`  | Delete. Keep doc comments only where a tool reads them.  |
| Summary doc on non-public member | `/// <summary>Stores the platform ids...</summary>` on a private method | Delete. If the name doesn't say it, rename or split. |
| Parameter echo           | `@param userId The user id`                             | Delete, or rename the parameter if it is unclear.        |
| Step narration           | `// Step 2: save to database`                           | Delete, or extract a function named after the step.      |
| Section banner or region | `// ===== Validation =====`, `#region Helpers`          | Split the unit; the banner marks a missing type or file. |
| Change note              | `// Changed to async`, `// Fixed null check`, `// New:` | Move to the commit message.                              |
| Comment as a name        | `var t = 30; // timeout in seconds`                     | Rename (`timeoutSeconds`) or use a typed duration.       |
| Essay comment            | A paragraph of design rationale above a method          | Move to `docs/<topic>.md` or a decision record.          |
| Untracked TODO           | `// TODO: handle errors properly`                       | Do it now, or open an issue and delete the line.         |

## Environment (E)

| Code | Smell                             | What to do                                                                           |
|------|-----------------------------------|--------------------------------------------------------------------------------------|
| E1   | Build requires more than one step | Check out and build with one obvious command; no hunting for extra scripts or files. |
| E2   | Tests require more than one step  | Run every test with one command or one click.                                        |

## Functions (F)

| Code | Smell              | What to do                                                                                     |
|------|--------------------|------------------------------------------------------------------------------------------------|
| F1   | Too many arguments | Prefer zero to two; three needs justification. Group related arguments into a named type.      |
| F2   | Output arguments   | Readers expect arguments to be inputs. Return the value or change the owning object's state.   |
| F3   | Flag arguments     | A boolean argument means the function does two things. Split it into two well-named functions. |
| F4   | Dead function      | A function nobody calls is noise. Delete it.                                                   |

## General (G)

| Code | Smell                                                  | What to do                                                                                                                  |
|------|--------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------|
| G1   | Multiple languages in one source file                  | Keep each file to one language where possible; minimize embedded SQL, HTML, scripts in strings.                             |
| G2   | Obvious behavior is unimplemented                      | Follow the principle of least surprise: `dayFromName("Monday")` should also accept `"monday"` if users would expect it.     |
| G3   | Incorrect behavior at the boundaries                   | Don't trust intuition at edges; test every boundary and corner case.                                                        |
| G4   | Overridden safeties                                    | Don't silence warnings, skip failing tests or disable checks to get green. Fix the cause.                                   |
| G5   | Duplication                                            | Every duplicate is a missed abstraction: extract a function, a type, polymorphism or a template method.                     |
| G6   | Code at the wrong level of abstraction                 | Keep high-level concepts in base types/modules and details in derived or lower ones; never mix them.                        |
| G7   | Base classes depending on their derivatives            | A base type must know nothing about its subtypes, so they can be deployed and changed independently.                        |
| G8   | Too much information                                   | Keep interfaces small: few functions, few public fields, few variables. Hide what callers don't need.                       |
| G9   | Dead code                                              | Unreachable branches, unused variables and conditions that never happen. Delete them.                                       |
| G10  | Vertical separation                                    | Declare variables and private helpers just above or near their use.                                                         |
| G11  | Inconsistency                                          | Do similar things the same way (same naming, same shape) so readers can predict the code.                                   |
| G12  | Clutter                                                | Remove empty constructors, unused variables, meaningless comments and anything else that adds nothing.                      |
| G13  | Artificial coupling                                    | Don't put things together just because it was convenient (a general enum inside a specific class).                          |
| G14  | Feature envy                                           | A method that mostly uses another object's data probably belongs on that object.                                            |
| G15  | Selector arguments                                     | Any argument (boolean, enum, int) that selects behavior: split into separate functions.                                     |
| G16  | Obscured intent                                        | No cryptic expressions, magic numbers or Hungarian notation. Make the intent visible.                                       |
| G17  | Misplaced responsibility                               | Put code where a reader would look for it, named after what it does, not where it was handy.                                |
| G18  | Inappropriate static                                   | Use a free/static function only when it never needs polymorphic behavior; otherwise make it an instance method.             |
| G19  | Use explanatory variables                              | Break a complex calculation into intermediate values with meaningful names.                                                 |
| G20  | Function names should say what they do                 | If you must read the body to know what `date.add(5)` adds, rename it (`addDaysTo`, `daysLater`).                            |
| G21  | Understand the algorithm                               | Don't stop when the tests pass by trial and error; make sure you know why it works, then make that obvious.                 |
| G22  | Make logical dependencies physical                     | If a module relies on an assumption about another, ask the other module for it explicitly.                                  |
| G23  | Prefer polymorphism to if/else or switch/case          | One switch per type selection, in a factory; elsewhere dispatch polymorphically.                                            |
| G24  | Follow standard conventions                            | Follow the team's and language's conventions; the code itself is the coding standard.                                       |
| G25  | Replace magic numbers with named constants             | Also magic strings and tokens. Exceptions: self-evident values in self-explanatory formulas.                                |
| G26  | Be precise                                             | Don't assume the first query result is the only one, don't use floats for money, handle concurrency and nulls deliberately. |
| G27  | Structure over convention                              | Enforce design decisions with structure (abstract members, exhaustive matching) rather than naming conventions.             |
| G28  | Encapsulate conditionals                               | `if (shouldBeDeleted(timer))` beats `if (timer.hasExpired() && !timer.isRecurrent())`.                                      |
| G29  | Avoid negative conditionals                            | `if (buffer.shouldCompact())` beats `if (!buffer.shouldNotCompact())`.                                                      |
| G30  | Functions should do one thing                          | Split functions that contain sections doing different things.                                                               |
| G31  | Hidden temporal couplings                              | When calls must happen in order, make each step return what the next one needs (a bucket brigade).                          |
| G32  | Don't be arbitrary                                     | Structure code for a reason the reader can see; if the structure looks arbitrary, others will change it.                    |
| G33  | Encapsulate boundary conditions                        | Compute `nextLevel = level + 1` once instead of scattering `+1` and `-1` through the code.                                  |
| G34  | Functions should descend only one level of abstraction | Statements in a function sit one level below the function's name.                                                           |
| G35  | Keep configurable data at high levels                  | Defaults and settings live at the top (entry point, configuration) and are passed down.                                     |
| G36  | Avoid transitive navigation                            | Law of Demeter: talk to immediate collaborators; no `a.getB().getC().doSomething()`.                                        |

## Language items (J)

The book's Java items, generalized. Each language skill states its own modern answer.

| Code | Book item                            | General lesson                                                                                                         |
|------|--------------------------------------|------------------------------------------------------------------------------------------------------------------------|
| J1   | Avoid long import lists by wildcards | Keep import lists from dominating the file; follow the language's tooling, which often prefers explicit imports today. |
| J2   | Don't inherit constants              | Don't inherit or implement a type just to reach its constants; import or reference them explicitly.                    |
| J3   | Constants versus enums               | Use the language's enum or sum type instead of groups of integer constants; it can carry names, data and behavior.     |

## Names (N)

| Code | Smell                                                | What to do                                                                                                    |
|------|------------------------------------------------------|---------------------------------------------------------------------------------------------------------------|
| N1   | Choose descriptive names                             | Names are 90% of readability. Take the time to choose them and revisit them.                                  |
| N2   | Choose names at the appropriate level of abstraction | Name by what it means at this level, not how it is implemented (`connect(locator)`, not `dial(phoneNumber)`). |
| N3   | Use standard nomenclature where possible             | Use pattern names (`Decorator`, `Factory`) and the project's ubiquitous language.                             |
| N4   | Unambiguous names                                    | A name must not have several plausible meanings; a longer exact name beats a short vague one.                 |
| N5   | Use long names for long scopes                       | Short names for tiny scopes; the wider the scope, the more precise and longer the name.                       |
| N6   | Avoid encodings                                      | No type or scope prefixes; let the tooling show types.                                                        |
| N7   | Names should describe side effects                   | `createOrReturnOos()`, not `getOos()` when it may create one.                                                 |

## Tests (T)

| Code | Smell                                         | What to do                                                                         |
|------|-----------------------------------------------|------------------------------------------------------------------------------------|
| T1   | Insufficient tests                            | Test everything that could break, not only what is convenient.                     |
| T2   | Use a coverage tool                           | Use coverage to find untested branches and lines.                                  |
| T3   | Don't skip trivial tests                      | They are cheap and document behavior.                                              |
| T4   | An ignored test is a question about ambiguity | A skipped test marks an unclear requirement. Resolve it; don't let it linger.      |
| T5   | Test boundary conditions                      | Most bugs live at the edges: empty, one, many, min, max, off-by-one.               |
| T6   | Exhaustively test near bugs                   | Bugs cluster. When you find one, test the surrounding code thoroughly.             |
| T7   | Patterns of failure are revealing             | Order tests well so that the pattern of which ones fail points at the cause.       |
| T8   | Test coverage patterns can be revealing       | Looking at which code the passing tests don't execute can explain why others fail. |
| T9   | Tests should be fast                          | Slow tests don't get run. Keep them fast.                                          |