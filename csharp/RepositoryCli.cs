#!/usr/bin/env dotnet --

#:property AssemblyName=dotgit
#:property Version=0.1.1
#:property PackageId=FM.Dotgit
#:property Description="Git repository toolkit: clone and update many repositories at once."
#:property PackageTags=dotnet-tool;cli;git;repository

#:include ./models/Result.cs

#:package ConsoleAppFramework@*
#:package LibGit2Sharp@*
#:package PrettyConsole@*

using System.Diagnostics;
using ConsoleAppFramework;
using LibGit2Sharp;
using PrettyConsole;
using static ErrorCodes;
using Git = LibGit2Sharp.Commands;
using CC = System.ConsoleColor;
using Result = Dotfiles.Models.Result<ErrorCodes>;

ConsoleApp.Create().Run(args);

[RegisterCommands]
sealed class Commands {
    /// <summary>
    /// Update all Git repositories found in the specified path
    /// </summary>
    /// <param name="path">-p, Path to search for Git repositories. Default: current directory</param>
    /// <param name="branches">-b, Target branches to switch to (in order of priority). Default: develop, main, master.</param>
    /// <param name="remoteName">-r, Remote name to fetch from.</param>
    /// <param name="timeout">-t, Maximum seconds to wait for the remote of each repository.</param>
    /// <param name="cancellationToken"></param>
    [Command("update|u")]
    public async Task<int> UpdateAsync(
        string path = ".",
        [HideDefaultValue] string[]? branches = null, string remoteName = "origin", int timeout = 60,
        CancellationToken cancellationToken = default) {
        branches ??= ["develop", "main", "master"];
        var fullPath = Path.GetFullPath(path);
        Console.WriteLine("🚀 Starting Git repository update process...");
        Console.WriteLineInterpolated($"⚡ Search path: {CC.Cyan}{fullPath}{CC.Default}");
        Console.WriteLineInterpolated($"⚡ Timestamp: {CC.Cyan}{TimeProvider.System.GetLocalNow():O}{CC.Default}");
        Console.WriteLine();
        Console.WriteLine("🔍 Searching for Git repositories...");
        var updatedRepos = new HashSet<string>();
        var failedRepos = new HashSet<string>();
        var gitDirs =
            from folder in Directory.GetDirectories(path)
            let gitFolder = Path.Combine(folder, ".git")
            where Directory.Exists(gitFolder)
            orderby folder
            select folder;
        foreach (var repoPath in gitDirs) {
            if (cancellationToken.IsCancellationRequested) break;

            Debug.Assert(repoPath != null, nameof(repoPath) + " != null");
            switch (await Repository.Update(
                        repoPath, branches, remoteName, TimeSpan.FromSeconds(timeout), cancellationToken)) {
                case Result.Success:
                    updatedRepos.Add(repoPath);
                    break;
                default:
                    failedRepos.Add(repoPath);
                    break;
            }
        }

        // Summary
        Console.WriteLine("==================================");
        Console.WriteLine("📊 UPDATE SUMMARY");
        Console.WriteLine("==================================");
        Console.WriteLine($"✅ Successfully updated: {updatedRepos.Count} repositories");
        Console.WriteLine($"❌ Failed updates: {failedRepos.Count} repositories");
        Console.WriteLine($"🕐 Completed at: {DateTime.Now}");
        if (failedRepos.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("⚠️  Some repositories failed to update. Check the output above for details.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Clone a Git repository to a specified destination
    /// </summary>
    /// <param name="repositoryList">-l, List file containing repositories to clone, one line for repository</param>
    /// <param name="repositoryUrl">
    /// -u, URL of the Git repository to clone, fallback to env GIT_URL_DEFAULT.
    /// Example: git@ssh.dev.azure.com:v3/MyOrg/MyProject
    /// </param>
    /// <param name="destinationPath">-p, Local path where the repository will be cloned</param>
    /// <param name="branch">-b, Specific branch to clone.</param>
    /// <param name="timeout">-t, Maximum seconds to wait for cloning each repository.</param>
    /// <param name="cancellationToken"></param>
    [Command("clone|c")]
    public async Task<int> CloneAsync(
        string destinationPath,
        string repositoryList,
        [HideDefaultValue] string? repositoryUrl = null,
        [HideDefaultValue] string? branch = null,
        int timeout = 60,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(repositoryList)) return WriteError("Repository list is required");

        repositoryUrl ??= Environment.GetEnvironmentVariable("GIT_URL_DEFAULT");
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            return WriteError("Supply --repository-url or set GIT_URL_DEFAULT environment variable");

        repositoryUrl = repositoryUrl.TrimEnd('/');
        Console.WriteLineInterpolated($"🚀 Repository base URL: {CC.Cyan}{repositoryUrl}{CC.Default}");
        Console.WriteLineInterpolated($"📁 Destination path: {CC.Cyan}{destinationPath}{CC.Default}");
        if (!string.IsNullOrEmpty(branch))
            Console.WriteLineInterpolated($"🌿 Target branch: {CC.Cyan}{branch}{CC.Default}");

        if (!File.Exists(repositoryList)) return WriteError("Repository list file not found", repositoryList);
        if (await File.ReadAllLinesAsync(repositoryList, cancellationToken) is not { Length: > 0 } lines)
            return WriteError("Repository list file is empty", repositoryList);

        var repositories = lines.Select(repoName =>
                new CloneTask(repoName, destinationPath, repositoryUrl, new CloneOptions { BranchName = branch }))
            .ToArray();
        var cloneTimeout = TimeSpan.FromSeconds(timeout);
        var task = Task.WhenAll(repositories.Select(repo => Git.Clone(repo, cloneTimeout, cancellationToken)));
        var spinner = Spinner.Patterns.Braille;
        var spinnerLength = spinner.Count;
        var spinnerIndex = 0;
        Console.CursorVisible = false; // Hide cursor to see progress better
        while (!task.IsCompleted) {
            spinnerIndex = (spinnerIndex + 1) % spinnerLength;
            var index = spinnerIndex;
            Console.Overwrite(() =>
                Array.ForEach(repositories, repo => repo.Dump(spinner[index])), repositories.Length);
            await Task.Delay(25, cancellationToken);
        }

        Console.Overwrite(() =>
            Array.ForEach(repositories, repo => repo.Dump(spinner[spinnerIndex])), repositories.Length);

        Console.CursorVisible = true;
        Console.SkipLines(repositories.Length);
        if (repositories.Any(repo => repo.Status is Unknown or ErrorCodes.Timeout)) {
            Console.WriteLine("⚠️  Some repositories failed to clone. Check the output above for details.");
            return 1;
        }

        Console.WriteLine("🎉 All repositories cloned successfully!");
        return 0;
    }

    private static int WriteError(string message, string? param = null) {
        if (param is not null)
            Console.WriteLineInterpolated($"❌ {message}: {CC.Cyan}{param}{CC.Default}");
        else
            Console.WriteLineInterpolated($"❌ {message}");
        return 1;
    }
}

sealed class CloneTask(
    string name,
    string destinationBasePath,
    string repositoryBaseUrl,
    CloneOptions? options = null) {
    public string Name { get; } = name;
    public string Path { get; } = System.IO.Path.Combine(destinationBasePath, name);
    public string Url { get; } = $"{repositoryBaseUrl}/{name}";
    public CloneOptions Options { get; } = options ?? new();
    public ErrorCodes Status { get; set; } = InProgress;
    public string? ErrorMessage { get; set; }

    public void Dump(in string inProgressIcon) {
        switch (Status) {
            case Success:
                Console.WriteLineInterpolated(OutputPipe.Error, $"{CC.Green}✔︎{CC.DefaultForeground}  {Name}");
                break;
            case Duplicate:
                Console.WriteLineInterpolated(OutputPipe.Error, $"⚠️ {Name}: already exists");
                break;
            case ErrorCodes.Timeout:
                Console.WriteLineInterpolated(OutputPipe.Error, $"⏱️ {Name}: {ErrorMessage}");
                break;
            case Unknown when !string.IsNullOrEmpty(ErrorMessage):
                Console.WriteLineInterpolated(OutputPipe.Error, $"❌ {Name}: {ErrorMessage}");
                break;
            default:
                Console.WriteLineInterpolated(OutputPipe.Error,
                    $"{CC.Green}{inProgressIcon}{CC.DefaultForeground}  {Name}");

                break;
        }
    }
}

enum ErrorCodes {
    Success, InProgress, Duplicate, Unknown,
    InvalidGitRepo, NoPendingChange,
    BranchNotFound, RemoteNotFound, Timeout,
    Conflict, NotFastForward
}

static class Extensions {
    extension(Repository repo) {
        public static async Task<Result> Update(string? path, string[] branches, string remoteName,
            TimeSpan timeout,
            CancellationToken cancellationToken) {
            var repoName = Path.GetFileName(path);
            Console.WriteLineInterpolated($"📦 Processing repository: {CC.Cyan}{repoName}{CC.Default}");
            try {
                using var gitRepo = new Repository(path);
                var currentBranch = gitRepo.Head.FriendlyName;
                Console.WriteLineInterpolated($"    📍 Current branch: {CC.Cyan}{currentBranch}{CC.Default}");
                if (gitRepo.Stash() is Result.HasValue<string> { Value: var stashName }) {
                    Console.WriteLineInterpolated(
                        $"    💾 Uncomitted change stashed as: {CC.Cyan}{stashName}{CC.Default}");
                }

                var targetBranch = currentBranch;
                switch (gitRepo.PickAvailableBranches(branches, remoteName)) {
                    case Result.HasValue<Branch> { Value: var branch }:
                        if (branch.FriendlyName != currentBranch) {
                            Console.WriteLineInterpolated(
                                $"    🔄 Switching to branch: {CC.Cyan}{branch.FriendlyName}{CC.Default}");
                        }

                        targetBranch = branch.FriendlyName;
                        Git.Checkout(gitRepo, branch);
                        break;
                    case Result.Failure { Error: BranchNotFound }:
                        Console.WriteLineInterpolated(
                            $"    ⚠️ None of the target branches ({string.Join(", ", branches)}) found. Staying on: {CC.Cyan}{currentBranch}{CC.Default}.");

                        break;
                    case Result.Failure { Error: RemoteNotFound }:
                        Console.WriteLineInterpolated($"    ⚠️ Remote {CC.Cyan}{remoteName}{CC.Default} is not found.");
                        return RemoteNotFound;
                }

                Console.WriteLine("    📥 Fetching latest changes...");
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);
                switch (await gitRepo.UpdateFromRemoteBranch(remoteName, targetBranch, timeoutSource.Token)) {
                    case Result.Success:
                        Console.WriteLineInterpolated(
                            $"    ✅ Successfully updated {CC.Cyan}{targetBranch}{CC.Default}");

                        break;
                    case Result.Failure { Error: BranchNotFound }:
                        Console.WriteLineInterpolated(
                            $"    ⚠️ Branch {CC.Cyan}{targetBranch}{CC.Default} doesn't exist on remote {CC.Cyan}{remoteName}{CC.Default}.");

                        break;
                    case Result.Failure { Error: RemoteNotFound }:
                        Console.WriteLineInterpolated($"    ⚠️ Remote {CC.Cyan}{remoteName}{CC.Default} is not found.");
                        return RemoteNotFound;
                    case Result.Failure { Error: ErrorCodes.Conflict or NotFastForward or Unknown } failure:
                        return failure.Error;
                }

                return Success;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                Console.WriteLine($"    ⏱️ Timed out after {timeout.TotalSeconds}s");
                return ErrorCodes.Timeout;
            }
            catch (RepositoryNotFoundException) {
                Console.WriteLineInterpolated(
                    $"    ⚠️ Skipping {CC.Cyan}{repoName}{CC.Default} - not a valid git repository");

                return InvalidGitRepo;
            }
            catch (Exception ex) {
                Console.WriteLine($"    ❌ Error: {ex.Message}");
                return Unknown;
            }
        }

        public Result.WithValue<Branch> PickAvailableBranches(string[] branches, string remoteName) {
            if (repo.Network.Remotes[remoteName] is null) return RemoteNotFound;

            var currentBranch = repo.Head.FriendlyName;
            string? targetBranch = null;
            foreach (var branch in branches) {
                if (repo.Branches[branch] != null || repo.Branches[$"{remoteName}/{branch}"] != null) {
                    targetBranch = branch;
                    break;
                }
            }

            if (targetBranch == currentBranch) return repo.Branches[currentBranch];

            if (targetBranch == null) return BranchNotFound;

            if (repo.Branches[targetBranch] is { } localBranch) return localBranch;

            var remoteBranch = repo.Branches[$"{remoteName}/{targetBranch}"];
            var newBranch = repo.CreateBranch(targetBranch, remoteBranch.Tip);
            repo.Branches.Update(newBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
            return newBranch;
        }

        public async Task<Result> UpdateFromRemoteBranch(string remoteName, string branchName,
            CancellationToken cancellationToken) {
            var remote = repo.Network.Remotes[remoteName];
            if (remote is null) return RemoteNotFound;

            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification).ToArray();
            var gitDir = repo.Info.Path;
            await Task.Run(() => FetchInOwnRepository(gitDir, remoteName, refSpecs, cancellationToken),
                    cancellationToken)
                .WaitAsync(cancellationToken);

            var remoteBranchRef = repo.Branches[$"{remoteName}/{branchName}"];
            if (remoteBranchRef is null) return BranchNotFound;

            return repo.Integrate(remoteBranchRef);

            // The fetch owns its own handle, so a timed-out fetch that keeps running never touches a disposed repository.
            static void FetchInOwnRepository(string gitDir, string remoteName, string[] refSpecs,
                CancellationToken cancellationToken) {
                using var fetchRepo = new Repository(gitDir);
                var options = new FetchOptions();
                options.StopWhen(cancellationToken);
                Git.Fetch(fetchRepo, remoteName, refSpecs, options, null);
            }
        }

        public Result Integrate(Branch upstream) {
            var branchName = repo.Head.FriendlyName;
            var originalTip = repo.Head.Tip;
            var mergeBase = repo.ObjectDatabase.FindMergeBase(originalTip, upstream.Tip);
            if (mergeBase?.Sha == upstream.Tip.Sha) {
                Console.WriteLine("    👌 Already up to date");
                return Result.Ok();
            }

            if (repo.Config.BuildSignature(DateTimeOffset.Now) is not { } signature) {
                Console.WriteLine("    ❌ Git identity is not configured, set user.name and user.email");
                return Unknown;
            }

            var rebase = repo.Config.PullRebases(branchName);
            var strategy = repo.Config.PullFastForwardStrategy();
            if (mergeBase?.Sha == originalTip.Sha && (rebase || strategy != FastForwardStrategy.NoFastForward)) {
                Console.WriteLineInterpolated(
                    $"    ⏩ Fast-forwarding {CC.Cyan}{branchName}{CC.Default} to {CC.Cyan}{upstream.FriendlyName}{CC.Default}");

                var fastForwardOnly = new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly };
                repo.Merge(upstream, signature, fastForwardOnly);
                return Result.Ok();
            }

            if (rebase) {
                Console.WriteLineInterpolated(
                    $"    🔀 Rebasing {CC.Cyan}{branchName}{CC.Default} onto {CC.Cyan}{upstream.FriendlyName}{CC.Default}");

                var committer = new Identity(signature.Name, signature.Email);
                RebaseResult? result = null;
                string? failure = null;
                try {
                    result = repo.Rebase.Start(repo.Head, upstream, null, committer, new RebaseOptions());
                }
                catch (Exception ex) {
                    failure = ex.Message;
                }

                if (result?.Status == RebaseStatus.Complete) return Result.Ok();

                if (repo.Info.CurrentOperation != CurrentOperation.None) repo.Rebase.Abort();

                if (repo.Head.Tip.Sha != originalTip.Sha) repo.Reset(ResetMode.Hard, originalTip);

                if (failure is not null) {
                    Console.WriteLineInterpolated(
                        $"    ❌ Rebase failed ({failure}), rebase aborted and {CC.Cyan}{branchName}{CC.Default} left unchanged");

                    return Unknown;
                }

                Console.WriteLineInterpolated(
                    $"    ❌ Conflict while rebasing, rebase aborted and {CC.Cyan}{branchName}{CC.Default} left unchanged");

                return ErrorCodes.Conflict;
            }

            if (strategy == FastForwardStrategy.FastForwardOnly) {
                Console.WriteLineInterpolated(
                    $"    ⚠️ Cannot fast-forward {CC.Cyan}{branchName}{CC.Default} to {CC.Cyan}{upstream.FriendlyName}{CC.Default} (ff = only)");

                return NotFastForward;
            }

            Console.WriteLineInterpolated(
                $"    🔀 Merging {CC.Cyan}{upstream.FriendlyName}{CC.Default} into {CC.Cyan}{branchName}{CC.Default}");

            var merge = repo.Merge(upstream, signature, new MergeOptions { FastForwardStrategy = strategy });
            if (merge.Status != MergeStatus.Conflicts) return Result.Ok();

            repo.Reset(ResetMode.Hard, originalTip);
            Console.WriteLineInterpolated(
                $"    ❌ Conflict while merging, merge aborted and {CC.Cyan}{branchName}{CC.Default} left unchanged");

            return ErrorCodes.Conflict;
        }

        public Result.WithValue<string> Stash() {
            if (repo.RetrieveStatus() is not { IsDirty: true })
                return NoPendingChange;

            var stashName = $"auto_stash_{TimeProvider.System.GetLocalNow():yyyyMMdd_HHmm}";
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Stashes.Add(signature, stashName);
            return stashName;
        }
    }

    extension(Configuration config) {
        public bool PullRebases(string branchName) =>
            (config.Get<string>($"branch.{branchName}.rebase") ?? config.Get<string>("pull.rebase")) is { } entry
            && (entry.Value is null || !IsFalse(entry.Value));

        public FastForwardStrategy PullFastForwardStrategy() =>
            (config.Get<string>("pull.ff") ?? config.Get<string>("merge.ff"))?.Value switch {
                { } value when value.Equals("only", StringComparison.OrdinalIgnoreCase) =>
                    FastForwardStrategy.FastForwardOnly,
                { } value when IsFalse(value) => FastForwardStrategy.NoFastForward,
                _ => FastForwardStrategy.Default
            };

        static bool IsFalse(string value) =>
            value.ToLowerInvariant() is "false" or "no" or "off" or "0" or "";
    }

    extension(FetchOptionsBase options) {
        public void StopWhen(CancellationToken cancellationToken) {
            options.RepositoryOperationStarting = _ => !cancellationToken.IsCancellationRequested;
            options.OnProgress = _ => !cancellationToken.IsCancellationRequested;
            options.OnTransferProgress = _ => !cancellationToken.IsCancellationRequested;
        }
    }

    extension(Git) {
        public static async Task Clone(CloneTask repo, TimeSpan timeout, CancellationToken cancellationToken) {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try {
                if (Directory.Exists(repo.Path)) {
                    repo.Status = Duplicate;
                    return;
                }

                repo.Options.FetchOptions.StopWhen(timeoutSource.Token);
                await Task.Run(() => Repository.Clone(repo.Url, repo.Path, repo.Options), timeoutSource.Token)
                    .WaitAsync(timeoutSource.Token);
                repo.Status = Success;
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                repo.Status = ErrorCodes.Timeout;
                repo.ErrorMessage = $"timed out after {timeout.TotalSeconds}s";
                if (Directory.Exists(repo.Path)) Directory.Delete(repo.Path, true);
            }
            catch (Exception ex) {
                repo.Status = Unknown;
                repo.ErrorMessage = ex.Message;
            }
        }
    }
}
