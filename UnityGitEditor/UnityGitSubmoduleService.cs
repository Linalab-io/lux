using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Linalab.UnityGit.Editor
{
    public readonly struct GitSubmoduleEntry
    {
        public GitSubmoduleEntry(string statusChar, string commitHash, string path, string description)
        {
            StatusChar = statusChar ?? string.Empty;
            CommitHash = commitHash ?? string.Empty;
            Path = path ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public string StatusChar { get; }
        public string CommitHash { get; }
        public string Path { get; }
        public string Description { get; }

        public bool IsInitialized => StatusChar != "-";
        public bool IsModified => StatusChar == "+";
        public bool IsConflict => StatusChar == "U";
        public bool IsUpToDate => StatusChar == " ";
    }

    public sealed class GitSubmoduleSnapshot
    {
        public GitSubmoduleSnapshot(bool isRepository, string repositoryRoot, string errorMessage, IReadOnlyList<GitSubmoduleEntry> entries)
        {
            IsRepository = isRepository;
            RepositoryRoot = repositoryRoot ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Entries = entries ?? Array.Empty<GitSubmoduleEntry>();
        }

        public bool IsRepository { get; }
        public string RepositoryRoot { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<GitSubmoduleEntry> Entries { get; }
    }

    public static class UnityGitSubmoduleService
    {
        static readonly string[] StatusArguments = { "submodule", "status", "--recursive" };
        static readonly string[] InitArguments = { "submodule", "update", "--init", "--recursive" };
        static readonly string[] UpdateArguments = { "submodule", "update", "--recursive" };
        static readonly string[] ResolveRepoRootArguments = { "rev-parse", "--show-toplevel" };

        public static GitSubmoduleSnapshot ReadSubmodules(string projectRootOverride = null)
        {
            var projectRoot = ResolveProjectRoot(projectRootOverride);
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return new GitSubmoduleSnapshot(false, string.Empty, "Unity project root could not be resolved.", Array.Empty<GitSubmoduleEntry>());
            }

            var repoRootResult = GitCommandRunner.Execute(projectRoot, ResolveRepoRootArguments);
            if (!repoRootResult.Success)
            {
                return new GitSubmoduleSnapshot(false, string.Empty, NormalizeGitErrorMessage(repoRootResult.ErrorMessage), Array.Empty<GitSubmoduleEntry>());
            }

            var repoRoot = repoRootResult.Output.Trim();
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            {
                return new GitSubmoduleSnapshot(false, string.Empty, "Git repository root could not be resolved.", Array.Empty<GitSubmoduleEntry>());
            }

            var statusResult = GitCommandRunner.Execute(repoRoot, StatusArguments);
            if (!statusResult.Success)
            {
                return new GitSubmoduleSnapshot(false, repoRoot, NormalizeGitErrorMessage(statusResult.ErrorMessage), Array.Empty<GitSubmoduleEntry>());
            }

            return ParseStatusOutput(statusResult.Output, repoRoot);
        }

        public static GitCommandResult InitSubmodules(string repositoryRoot)
        {
            return GitCommandRunner.Execute(repositoryRoot, InitArguments);
        }

        public static GitCommandResult UpdateSubmodules(string repositoryRoot)
        {
            return GitCommandRunner.Execute(repositoryRoot, UpdateArguments);
        }

        public static GitCommandResult UpdateSubmodule(string repositoryRoot, string path)
        {
            return GitCommandRunner.Execute(repositoryRoot, "submodule", "update", "--init", "--", path);
        }

        public static GitSubmoduleSnapshot ParseStatusOutput(string output, string repositoryRoot)
        {
            var normalizedOutput = output ?? string.Empty;
            var lines = normalizedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var entries = new List<GitSubmoduleEntry>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Length < 42) continue;

                var statusChar = line.Substring(0, 1);
                var rest = line.Substring(1).TrimStart();
                
                var spaceIndex = rest.IndexOf(' ');
                if (spaceIndex < 0) continue;

                var commitHash = rest.Substring(0, spaceIndex);
                rest = rest.Substring(spaceIndex + 1).TrimStart();

                var path = rest;
                var description = string.Empty;

                var parenIndex = rest.IndexOf(" (", StringComparison.Ordinal);
                if (parenIndex >= 0)
                {
                    path = rest.Substring(0, parenIndex);
                    description = rest.Substring(parenIndex + 2).TrimEnd(')');
                }

                entries.Add(new GitSubmoduleEntry(statusChar, commitHash, path, description));
            }

            return new GitSubmoduleSnapshot(true, repositoryRoot, string.Empty, entries);
        }

        static string ResolveProjectRoot(string projectRootOverride)
        {
            if (!string.IsNullOrWhiteSpace(projectRootOverride))
            {
                return Path.GetFullPath(projectRootOverride);
            }

            var assetsPath = Application.dataPath;
            if (!string.IsNullOrWhiteSpace(assetsPath))
            {
                var assetsDirectory = new DirectoryInfo(assetsPath);
                if (assetsDirectory.Parent != null)
                {
                    return assetsDirectory.Parent.FullName;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        static string NormalizeGitErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return "Git submodule status could not be loaded.";
            }

            var trimmed = errorMessage.Trim();
            if (trimmed.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "No Git repository was found at or above the Unity project root.";
            }

            return trimmed;
        }
    }
}
