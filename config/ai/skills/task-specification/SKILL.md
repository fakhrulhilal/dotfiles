---
name: task-specification
description: "Write or rewrite a task / feature specification (a per-service breakdown, the overview of a cross-service change, or a 'how this service works' doc) in the house What → How → When → Where → Why format. Every service method, message topic, HTTP route, database table and cache key is labelled; frontend calls are tied to their gateway route; new contracts are highlighted; data contracts go in tables; there are no code pointers and the task list is short. Research is grounded in the code on the main/master branch (graphify first when a graph exists). Use whenever the user asks to write, break down, spec, document or restructure a change or service behaviour as a spec, task breakdown, 4W1H doc, or 'in the usual format', even if they don't name the format."
---

# Task specification

A spec says **what to achieve, how data flows between services, and which contracts change**. Developers who will
write the code read it, and so do people who will never open the code. It therefore describes contracts and
behaviour, not code.

The examples below use a made-up system: an `IdentityService` that caches user and tenant data and answers
"is this allowed?", an `AccountService` that owns the data, and a frontend. Substitute the real names of the
product you are working on.

## Research first: ground every claim in the code

Never write a contract, flow or "today it does X" from memory or from an older doc. Old docs and earlier breakdowns go
stale.

1. **Read the main branch.** Research `main` (or `master`) in every repo unless the user names another branch. Never
   switch the user's working tree to read it: use `git show main:<path>`, `git grep <pattern> main`, or a worktree.
   When the user names a branch, use that one and say so in the doc.
2. **Use graphify when the repo has a graph.** If `<repo>/graphify-out/graph.json` exists, load the `graphify` skill
   and query the graph first (`graphify query "<question>"`, `graphify path "A" "B"`, `graphify explain "X"`). The
   graph tells you which handler, consumer, service method or entity matters. Then read only those files to confirm
   the details (signatures, conditions, field names). If no graph exists, search directly; don't build a graph unless
   the user asks. A graph reflects the branch it was built from, so check that it matches the branch you are
   documenting.
3. **Verify cross-service facts at both ends:**
   - **Service methods:** the method exists on the serving side, and its response model has the shape you describe.
   - **Message topics:** the producer builds the topic name you write (any prefix included), and the consumer
     subscribes to it.
   - **Frontend calls:** the route exists in the gateway's routes config, the frontend's API layer calls that path,
     and the downstream method is the one you name.
   - **Shared libraries:** check every consumer's dependency constraints (language/runtime, framework, client
     library) before proposing a shared place.
4. **Say plainly what you could not verify.** If something can't be checked from code (two packages loading together,
   a runtime behaviour), make it an explicit spike task or open question instead of asserting it.

## Document structure

Sections are **H1**, in this order. Skip any section that would add nothing; a thin doc that defers to a sibling may
only have What, How, When and Tasks. Never reorder them.

```markdown
# <Service or topic>

[← Summary](summary.md) · <other back links>

# What
# How
# When
# Where
# Why
# Tasks
# Open questions
```

- The document title is also H1, on the first line, followed by the back links. Use the service name, with its
  agreed abbreviation if there is one ("IdentityService (IDS)"), or a short topic name for an overview.
- Subsections inside a section are H2 (`## Sync contract`, `## Data contracts to adjust`), and H3 below that.
- An overview doc may start with a short list of links to its sections.
- A sub-topic that describes one phase of the overall change (for example "what happens when access ends") belongs as
  an H2 under the overview's **When**, not in a separate doc.

### What

At a glance, the **goal**: what must be achieved and for whom. No technical detail, and no method or topic names
unless they are the subject itself. Use a bullet list of outcomes, plus small tables when they clarify the goal (a
state × behaviour matrix, a category → effect classification).

### How

Technical implementation at a glance, focused on **communication between parts**:

- which service methods are called or served, which message topics are produced or consumed, and what each carries;
- how data and context are shared: who owns it, who copies it, who only asks;
- the expected data on each hop: show the payload or response shape as a small table, a one-line signature
  (`is_notification_allowed { user_id, tenant_id, subject } → true | false`) or a short JSON example;
- decision rules as a table or a short pseudo-rule block ("first match wins");
- a mermaid `sequenceDiagram` or `flowchart` when more than two parties exchange data.

End How with a **`## Data contracts to adjust`** table whenever anything changes:

| Contract | Change |
| --- | --- |
| **Method** `get_tenant_status` (**HTTP** `GET /v2/tenants/{tenant_id}/status`) | **New** field `is_active` |
| **Topic** `account.tenant_user.updated` | Unchanged schema; new reasons to produce |
| **Method** `is_notification_allowed` | **New** method; internal route **HTTP** `GET /v2/utils/identity/is_notification_allowed` |

Put error codes, configuration keys, routes, persisted properties and storage changes in that table too.

### When

When the data flows and is processed: triggers, schedules, ordering, latency, and what happens at each moment. A
"Moment | Data flow | Effect" table works well. Fold the **rollout order** in here as a numbered list ending in a
sentence about when behaviour first changes for users.

### Where

A summary table of where data is transferred and processed: every service method (with its gateway route when it has
one), message topic, consumer group, deployment, database, cache and external system involved.

| Item | Status | Role |
| --- | --- | --- |
| **Topic** `account.tenant.updated` | existing, produced | Tenant deactivated |
| **Method** `is_notification_allowed` (**HTTP** `GET /v2/utils/identity/is_notification_allowed`) | **new**, served | The question |

- For a **frontend** doc, use a "Gateway route | Service | Method | Use" table listing every route the frontend calls
  for the change.
- For an **overview**, use a "Service | Role | Talks over" table with links to each service doc, plus one line
  listing the services with no change and why.

### Why

The importance of each part: why this method, topic, rule, order or storage exists, and what breaks without it. Write
bullets that start with a bold reason. Fold in here:

- the **decisions** taken (a "Topic | Decision" table);
- rejected alternatives and why;
- **accepted limitations** (what we deliberately don't do, and the user-visible consequence);
- known gaps or bugs, framed as their consequence;
- load or cost notes.

### Tasks

A short table, one line per task, no code:

| ID | Task |
| --- | --- |
| ACC-1 | Access end date: new setting; set it when the account is first created on the new system, never change it later. |

- IDs are `<SERVICE PREFIX>-<n>`, numbered with no gaps; renumber when a task is dropped and update every reference.
- Keep a final tests task that lists the scenarios, not test names.
- A spike task comes before any task that depends on something you couldn't verify.
- An overview doc has a "Service | Tasks | Doc" table linking to each doc's `#tasks`, unless the user removed it.
- Docs that only describe current behaviour have no Tasks.

### Open questions

Bullets, each starting with a bold short title, stating what is undecided and what the doc assumes meanwhile. Put each
question in the doc of the service that owns it (UI wording and URLs in the frontend doc). The overview keeps only the
cross-cutting ones and links to the rest. When the user answers a question, turn it into a decision (in Why) and
update every doc it affects.

## Design guidelines (where things belong)

Apply these before the user has to ask.

- **Scope.** Cover only the products or services the user names. Delete docs and remove mentions for anything out of
  scope; don't keep "same for product X" stubs.
- **Owner holds the source of truth; the decider holds a copy.** Persist a value in the service that writes it and
  that the frontend reads it from. Backend-to-backend questions are answered from the deciding service's cached copy.
  Say explicitly who reads what from where (a "Reader | Reads from | Use" table).
- **Name contract fields by what consumers decide, not by how they are derived.** Write `is_active`, not
  `first_user_deactivated_at`. The rule that sets the field is written once, next to the field.
- **Match the method to the caller and the level.** Before adding a field, find the method that caller already uses
  (check the frontend's API layer and the gateway routes). Don't put tenant-level data in a user-level method, or the
  reverse.
- **Frontend vs internal routes.** Every method a frontend calls is shown with its gateway route
  (**HTTP** `METHOD /vN/path` → **Method** `name`), taken from the routes config. Use the service name the gateway
  registers, which can differ from the repo name. A method no frontend calls, new or existing, is exposed (if at all)
  only under the project's internal/utility route prefix (for example `/utils/<service>/…`).
- **Check before doing expensive work.** When a change adds a guard, put it at the earliest point in every producer,
  before content is built. A central check downstream is only a safety net, never the main guard. Never mark a
  producer "no change because something downstream checks".
- **One shared implementation per stack.** When several services in the same language need the same check, specify
  one helper in the package they already share. Check dependency compatibility, and give a fallback if one consumer
  can't take it.
- **Reuse existing infrastructure first.** Prefer existing topics, consumers and refresh mechanisms with new fields
  over new topics. Say "existing, unchanged schema" when you do.
- **Cost versus benefit.** If a part only improves UX (for example showing a reason for an already-enforced refusal)
  and costs a separate service change, flag it as optional and ask. When the user drops it, record the user-visible
  consequence as an accepted limitation.
- **Interpret ambiguous wording explicitly.** If an instruction can be read two ways (for example a boolean's
  direction), pick the reading consistent with the field's name, apply it, and say which reading you used.

## Labels: always say what kind of thing a name is

Every reference to a named part gets a bold label right before its backticked name, everywhere in the text and tables
(not inside code blocks or mermaid):

| Label | For | Example |
| --- | --- | --- |
| **Method** | Service methods (RPC, gRPC, JSON-RPC, …) | **Method** `validate_login` |
| **Topic** | Message topics / queues, including short forms | **Topic** `tenant_user.updated` |
| **DB** | Database tables | **DB** `message_queue` |
| **HTTP** | Gateway / REST routes, with version prefix | **HTTP** `POST /v2/tenants/query` |
| **Cache** | Cache keys | **Cache** `cache_user_version:{user_id}` |
| **Index** | Search indices | **Index** `tenant_user` |

- Drop words the label makes redundant. Write **Topic** `notification`, not "the `notification` topic" or "message
  topic `notification`"; write **Method** `send_report`, not "RPC `send_report`".
- Do not label field names, subjects, error codes, cron expressions, config keys or enum values.
- Add a label for another kind of part (for example **File** for an object-storage path) only when it is named often
  enough to need one; define it once.

## Highlight what is new

- Anything introduced by the change is marked **new**: "**New** **Method** `patch_access_end_date`",
  "**new** field `is_active`", "**New** error code `40901`".
- When an existing topic, method or route is reused, say so explicitly ("existing, unchanged schema", "the frontend
  already calls it"), so readers know no new infrastructure is needed.

## What not to write

- **No code pointers**: no file paths, line numbers, class, function or variable names, config file paths, or
  internals of a particular branch. Describe behaviour instead ("the existing refresh handler re-reads the context").
- These **are** contracts, so keep them: method names, topic names, routes, table names, subjects, error codes, config
  keys, payload fields, and persisted properties of a stored document (`Tenant.Settings.IsActive`).
- Don't restate the same fact in several sections: say it once where it belongs and link to it.

## Style

- Plain, concise English; short sentences; tables where there are more than two parallel items.
- Use the domain's terms consistently (pick "tenant" or "organization", not both). Introduce a service abbreviation
  once and use it from then on.
- Times in UTC, durations explicit ("~60 s", "up to about 2 hours").
- Links between docs point to a doc or one of its sections (`other.md#how`, `other.md#tasks`).

## Editing an existing set of docs

1. Read every doc in scope first, plus the code it describes (see "Research first"). Re-read a file before
   overwriting it if it may have changed; the user edits these docs too. Keep their edits (removed sections, renamed
   headings, their wording) and adapt to them rather than restoring yours.
2. Map the old content into the five sections; move extras as above (classification → What, rollout → When,
   decisions, limitations and gaps → Why, open questions last). Keep every fact; drop only code locations.
3. When a design changes, update **every** affected doc in the same pass: overview, each service doc, task IDs and
   cross-references. Then search for the old names (`grep`) to make sure none is left.
4. Apply the labels and **new** markers.
5. Check that every relative link and anchor still resolves, and that no table cell contains an unescaped `|` (write
   "`a` or `b`" instead of `` `a|b` ``). For example:

   ```bash
   python3 - <<'EOF'
   import re, os, glob
   slug = lambda h: re.sub(r'[^\w\- ]', '', h.strip().lower()).replace(' ', '-')
   bad = []
   for f in glob.glob('**/*.md', recursive=True):
       for link in re.findall(r'\]\(([^)]+)\)', open(f).read()):
           if link.startswith('http'): continue
           p, _, a = link.partition('#')
           t = os.path.normpath(os.path.join(os.path.dirname(f), p)) if p else f
           if not os.path.exists(t): bad.append((f, link)); continue
           if a and a not in {slug(m) for m in re.findall(r'^#+ (.*)$', open(t).read(), re.M)}:
               bad.append((f, link))
   print(bad or 'all links ok')
   EOF
   ```

6. Report per doc what changed, any fact that had to be dropped, anything you could not verify, and any reading of an
   ambiguous instruction you had to choose.
