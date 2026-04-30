using Linalab.UnityGit.Editor;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Linalab.UnityGit.Editor.Tests
{
    public class UnityGitBranchServiceTests
    {
        [Test]
        public void ReadBranches_WithLocalBranches_ReturnsCurrentBranchAndBranchList()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("branch-source.txt", "branch base");
            repository.AssertGitSuccess("branch", "feature/test-branch");

            var snapshot = UnityGitBranchService.ReadBranches(repository.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.True, snapshot.ErrorMessage);
            Assert.That(snapshot.Branches, Is.Not.Empty);
            Assert.That(snapshot.Branches.Any(branch => branch.Name == "feature/test-branch"), Is.True);
            Assert.That(snapshot.Branches.Any(branch => branch.IsCurrent), Is.True);
            Assert.That(snapshot.CurrentBranchName, Is.Not.Empty);
            Assert.That(snapshot.Branches.Any(branch => branch.Name == snapshot.CurrentBranchName && branch.IsCurrent), Is.True);
        }

        [Test]
        public void SwitchBranch_WithCleanWorkingTree_ChangesCurrentBranch()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("switch-source.txt", "switch base");
            repository.AssertGitSuccess("branch", "feature/test-branch");

            var beforeSnapshot = UnityGitBranchService.ReadBranches(repository.RepositoryPath);
            Assert.That(beforeSnapshot.CurrentBranchName, Is.Not.EqualTo("feature/test-branch"));

            var switchResult = UnityGitBranchService.SwitchBranch(repository.RepositoryPath, "feature/test-branch");

            Assert.That(switchResult.Success, Is.True, switchResult.ErrorMessage);

            var afterSnapshot = UnityGitBranchService.ReadBranches(repository.RepositoryPath);
            Assert.That(afterSnapshot.CurrentBranchName, Is.EqualTo("feature/test-branch"));
            Assert.That(afterSnapshot.Branches.Any(branch => branch.Name == "feature/test-branch" && branch.IsCurrent), Is.True);
        }

        [Test]
        public void SwitchBranch_WithDirtyWorkingTree_ReturnsFailureAndKeepsChanges()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("dirty-source.txt", "dirty base");
            repository.AssertGitSuccess("branch", "feature/test-branch");
            repository.AssertGitSuccess("checkout", "feature/test-branch");
            repository.WriteFile(Path.Combine("Assets", "dirty-source.txt"), "feature branch content");
            repository.AssertGitSuccess("add", "--all");
            repository.AssertGitSuccess("commit", "-m", "feature change");
            repository.AssertGitSuccess("checkout", "-");

            repository.WriteFile(Path.Combine("Assets", "dirty-source.txt"), "dirty worktree change");

            var sourceFilePath = repository.GetPath("Assets", "dirty-source.txt");
            var originalContents = File.ReadAllText(sourceFilePath);
            var switchResult = UnityGitBranchService.SwitchBranch(repository.RepositoryPath, "feature/test-branch");

            Assert.That(switchResult.Success, Is.False);
            Assert.That(switchResult.ErrorMessage, Is.Not.Empty);
            Assert.That(File.ReadAllText(sourceFilePath), Is.EqualTo(originalContents));

            var snapshot = UnityGitBranchService.ReadBranches(repository.RepositoryPath);
            Assert.That(snapshot.CurrentBranchName, Is.Not.EqualTo("feature/test-branch"));
        }

        [Test]
        public void ReadBranches_OutsideRepository_ReturnsFriendlyMessage()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace();

            var snapshot = UnityGitBranchService.ReadBranches(workspace.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.False);
            Assert.That(snapshot.RepositoryRoot, Is.Empty);
            Assert.That(snapshot.CurrentBranchName, Is.Empty);
            Assert.That(snapshot.ErrorMessage, Is.EqualTo("No Git repository was found at or above the Unity project root."));
            Assert.That(snapshot.Branches, Is.Empty);
        }
    }
}
