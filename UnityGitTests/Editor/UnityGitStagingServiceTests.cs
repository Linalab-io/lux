using Linalab.UnityGit.Editor;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Linalab.UnityGit.Editor.Tests
{
    public class UnityGitStagingServiceTests
    {
        [Test]
        public void StagePath_WithModifiedFile_StagesSinglePath()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("alpha.txt", "alpha base");
            var relativePath = "Assets/alpha.txt";
            repository.WriteFile(relativePath, "alpha changed");

            var result = UnityGitStagingService.StagePath(repository.RepositoryPath, relativePath);

            Assert.That(result.Success, Is.True, result.ErrorMessage);

            var snapshot = UnityGitStatusService.ReadStatus(repository.RepositoryPath);
            var entry = GetEntry(snapshot, relativePath);

            Assert.That(entry.IsStaged, Is.True);
            Assert.That(entry.IsUnstaged, Is.False);
        }

        [Test]
        public void UnstagePath_WithStagedFile_ReturnsToUnstagedState()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("beta.txt", "beta base");
            var relativePath = "Assets/beta.txt";
            repository.WriteFile(relativePath, "beta changed");

            var stageResult = UnityGitStagingService.StagePath(repository.RepositoryPath, relativePath);
            Assert.That(stageResult.Success, Is.True, stageResult.ErrorMessage);

            var unstageResult = UnityGitStagingService.UnstagePath(repository.RepositoryPath, relativePath);

            Assert.That(unstageResult.Success, Is.True, unstageResult.ErrorMessage);

            var snapshot = UnityGitStatusService.ReadStatus(repository.RepositoryPath);
            var entry = GetEntry(snapshot, relativePath);

            Assert.That(entry.IsStaged, Is.False);
            Assert.That(entry.IsUnstaged, Is.True);
        }

        [Test]
        public void StageAll_AndUnstageAll_HandleMultipleChangedFiles()
        {
            using var repository = UnityGitTestRepository.CreateCommitted(
                new Dictionary<string, string>
                {
                    { "first.txt", "first base" },
                    { "second.txt", "second base" }
                });
            var firstPath = "Assets/first.txt";
            var secondPath = "Assets/second.txt";
            repository.WriteFile(firstPath, "first changed");
            repository.WriteFile(secondPath, "second changed");

            var stageAllResult = UnityGitStagingService.StageAll(repository.RepositoryPath);

            Assert.That(stageAllResult.Success, Is.True, stageAllResult.ErrorMessage);

            var stagedSnapshot = UnityGitStatusService.ReadStatus(repository.RepositoryPath);
            var stagedEntries = new[] { GetEntry(stagedSnapshot, firstPath), GetEntry(stagedSnapshot, secondPath) };

            foreach (var entry in stagedEntries)
            {
                Assert.That(entry.IsStaged, Is.True);
                Assert.That(entry.IsUnstaged, Is.False);
            }

            var unstageAllResult = UnityGitStagingService.UnstageAll(repository.RepositoryPath);

            Assert.That(unstageAllResult.Success, Is.True, unstageAllResult.ErrorMessage);

            var unstagedSnapshot = UnityGitStatusService.ReadStatus(repository.RepositoryPath);
            var unstagedEntries = new[] { GetEntry(unstagedSnapshot, firstPath), GetEntry(unstagedSnapshot, secondPath) };

            foreach (var entry in unstagedEntries)
            {
                Assert.That(entry.IsStaged, Is.False);
                Assert.That(entry.IsUnstaged, Is.True);
            }
        }

        [Test]
        public void StageAll_OutsideRepository_ReturnsFailure()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace();

            var result = UnityGitStagingService.StageAll(workspace.RepositoryPath);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.Not.Empty);
        }

        static GitStatusEntry GetEntry(GitStatusSnapshot snapshot, string relativePath)
        {
            foreach (var entry in snapshot.Entries)
            {
                if (string.Equals(entry.Path, relativePath, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            Assert.Fail($"Missing status entry for {relativePath}.");
            return default;
        }

    }
}
