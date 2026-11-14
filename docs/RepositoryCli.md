# RepositoryCli (`dotgit`)

Manages Git repositories in batch, for when a project is spread over many repositories:

- **update** every repository inside a single folder, so all of them sit on their remote branch with the latest changes;
- **clone** a set of repositories from a single source, such as an Azure DevOps project or a GitHub organization.

It works through LibGit2Sharp, so it needs no `git` executable. Source:
[`csharp/RepositoryCli.cs`](../csharp/RepositoryCli.cs).

```
dotgit <command> [options]
```

| Command     | Alias | Purpose                                                                   |
|-------------|-------|---------------------------------------------------------------------------|
| `update`    | `u`   | Switch every repository in a folder to its main branch and pull it        |
| `clone`     | `c`   | Clone every repository named in `repositories.txt` from one base URL      |

It has no mise alias or release yet. Run it from inside `csharp/`:

```shell
dotnet run RepositoryCli.cs -- update --path ~/Projects
dotnet run RepositoryCli.cs -- clone --destination-path ~/Projects
```

## `update`

Looks at the folders directly inside `--path` (one level deep, sorted by name) and updates each one that has a `.git`
folder. Repositories are processed one at a time.

| Option                | Default                     | Notes                                             |
|-----------------------|-----------------------------|---------------------------------------------------|
| `-p`, `--path`        | `.`                         | Folder that holds the repositories                |
| `-b`, `--branches`    | `develop`, `main`, `master` | Branches to switch to, in order of priority       |
| `-r`, `--remote-name` | `origin`                    | Remote to fetch from                              |
| `-t`, `--timeout`     | `60`                        | Seconds to wait for the remote of each repository |

```shell
# every repository under ~/Projects/work, on develop, main or master (the defaults)
dotgit update --path ~/Projects/work

# repositories that use main as the trunk, with a shorter wait for a slow VPN
dotgit u -p ~/Projects/work -b main -t 30
```

For each repository it:

1. **Stashes uncommitted changes**, as `auto_stash_<yyyyMMdd_HHmm>`. The stash is not popped afterwards; run
   `git stash pop` yourself when you need the changes back. Untracked files are not stashed and stay where they are.
2. **Picks a branch**: the first entry of `--branches` that exists locally or as `<remote>/<branch>`. When it only
   exists on the remote, a local branch tracking it is created. When none exists, the repository stays on its current
   branch. Remote branches are those known from the last fetch, so a branch created on the server since then is only
   seen on the next run.
3. **Fetches** the remote with its configured refspecs, stopping after `--timeout` seconds.
4. **Integrates** `<remote>/<branch>` into the local branch, following your Git configuration like `git pull` does:

   | Situation                                                             | What happens                               |
   |-----------------------------------------------------------------------|--------------------------------------------|
   | Local branch already contains the remote branch                       | Nothing, "Already up to date"              |
   | Local branch is behind, and rebase is on or `ff` is not `false`       | Fast-forward                               |
   | `branch.<name>.rebase` or `pull.rebase` is set and not false          | Rebase onto the remote branch              |
   | `ff = only` and the branches diverged                                 | Fails, branch left as it was               |
   | Otherwise                                                             | Merge, as a merge commit when `ff = false` |

   `ff` is read from `pull.ff`, falling back to `merge.ff`.

   A rebase or merge that hits a conflict is aborted and the branch is reset to where it was, so a repository is never
   left half-merged. Rebasing and merging need `user.name` and `user.email` in your Git configuration.

A repository counts as **failed** when it has no remote of that name, the fetch times out, the rebase or merge
conflicts, it can't be fast-forwarded under `ff = only`, or any other error occurs. A branch that doesn't exist on the
remote only prints a warning. A summary of updated and failed repositories closes the run.

Ctrl+C stops after the repository that is being processed.

## `clone`

Clones every repository listed in `repositories.txt` into a folder, all at once, with a live progress line per
repository.

| Option                     | Default           | Notes                                                           |
|----------------------------|-------------------|-----------------------------------------------------------------|
| `-p`, `--destination-path` | (required)        | Folder to clone into. Each repository goes into `<path>/<name>` |
| `-u`, `--repository-url`   | `GIT_URL_DEFAULT` | Base URL; each repository is cloned from `<url>/<name>`         |
| `-b`, `--branch`           | remote's default  | Branch to check out in every clone                              |
| `-t`, `--timeout`          | `60`              | Seconds to wait for each clone                                  |

```shell
# every repository of an Azure DevOps project, with the URL set once in ~/.zshenv
export GIT_URL_DEFAULT=git@ssh.dev.azure.com:v3/MyOrg/MyProject
dotgit clone --destination-path ~/Projects/work

# the same list from a GitHub organization, checked out on develop
dotgit c -p ~/Projects/work -u git@github.com:my-org -b develop
```

### The repository list

The list is read from `configurations/repositories.txt`, one folder up from the script (`csharp/../configurations/` when
run from source) or from the binary's folder when installed. It holds one repository name per line, without blank
lines, and is not part of the repository, so create it yourself:

```text
api-gateway
billing-service
web-frontend
```

With the URL above, `billing-service` is cloned from `git@ssh.dev.azure.com:v3/MyOrg/MyProject/billing-service` into
`~/Projects/work/billing-service`.

### Results

| Mark | Meaning                                                                                  |
|------|------------------------------------------------------------------------------------------|
| ✔︎    | Cloned                                                                                   |
| ⚠️    | The destination folder already exists, so it was skipped. Not counted as a failure       |
| ⏱️    | Timed out. The partly cloned folder is deleted                                           |
| ❌    | Failed, with the error message (wrong URL, no access, unknown branch, ...)               |

SSH URLs use your SSH agent and keys; HTTPS URLs need a remote that allows the clone without prompting.

## Exit codes

Both commands exit with `0` when everything succeeded and `1` when at least one repository failed. `clone` also exits
with `1` when the repository list is missing or empty.
