using Linalab.UnityGit.Editor;
using NUnit.Framework;
using System.IO;

namespace Linalab.UnityGit.Editor.Tests
{
    public class UnityGitRemoteServiceTests
    {
        [Test]
        public void PullCurrentBranch_WithFastForwardUpdate_UpdatesLocalRepositoryAndSignalsRefresh()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace("unity-git-remote-test");
            var originPath = workspace.CreateBareRepository();
            var seedClonePath = CreateClonedRepository(workspace, originPath, "seed", "base.txt", "base content", "base commit", true);
            var testClonePath = CreateClonedRepository(workspace, originPath, "clone", null, null, null, false);

            UnityGitTestRepository.WriteFile(seedClonePath, Path.Combine("Assets", "remote.txt"), "upstream content");
            UnityGitTestRepository.AssertGitSuccess(seedClonePath, "add", "--all");
            UnityGitTestRepository.AssertGitSuccess(seedClonePath, "commit", "-m", "upstream commit");
            UnityGitTestRepository.AssertGitSuccess(seedClonePath, "push");

            var refreshCount = 0;
            var originalRefreshAssets = UnityGitRemoteService.RefreshProjectAssets;
            var originalRefreshStatus = UnityGitRemoteService.RefreshStatus;

            try
            {
                UnityGitRemoteService.RefreshProjectAssets = () => refreshCount++;
                UnityGitRemoteService.RefreshStatus = () => refreshCount++;

                var result = UnityGitRemoteService.PullCurrentBranch(testClonePath);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.ShouldRefreshStatus, Is.True);
                Assert.That(refreshCount, Is.EqualTo(2));
                Assert.That(UnityGitTestRepository.ReadGitOutput(testClonePath, "rev-parse", "HEAD"), Is.EqualTo(UnityGitTestRepository.ReadGitOutput(seedClonePath, "rev-parse", "HEAD")));
                Assert.That(File.Exists(Path.Combine(testClonePath, "Assets", "remote.txt")), Is.True);
            }
            finally
            {
                UnityGitRemoteService.RefreshProjectAssets = originalRefreshAssets;
                UnityGitRemoteService.RefreshStatus = originalRefreshStatus;
            }
        }

        [Test]
        public void PushCurrentBranch_WithNewLocalCommit_UpdatesOriginRepository()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace("unity-git-remote-test");
            var originPath = workspace.CreateBareRepository();
            CreateClonedRepository(workspace, originPath, "seed", "base.txt", "base content", "base commit", true);
            var testClonePath = CreateClonedRepository(workspace, originPath, "clone", null, null, null, false);

            UnityGitTestRepository.WriteFile(testClonePath, Path.Combine("Assets", "push.txt"), "push content");
            UnityGitTestRepository.AssertGitSuccess(testClonePath, "add", "--all");
            UnityGitTestRepository.AssertGitSuccess(testClonePath, "commit", "-m", "push commit");

            var refreshCount = 0;
            var originalRefreshStatus = UnityGitRemoteService.RefreshStatus;

            try
            {
                UnityGitRemoteService.RefreshStatus = () => refreshCount++;

                var result = UnityGitRemoteService.PushCurrentBranch(testClonePath);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.ShouldRefreshStatus, Is.True);
                Assert.That(refreshCount, Is.EqualTo(1));
                Assert.That(UnityGitTestRepository.ReadGitOutput(originPath, "rev-parse", "HEAD"), Is.EqualTo(UnityGitTestRepository.ReadGitOutput(testClonePath, "rev-parse", "HEAD")));
            }
            finally
            {
                UnityGitRemoteService.RefreshStatus = originalRefreshStatus;
            }
        }

        [Test]
        public void PullCurrentBranch_WithNonFastForwardHistory_ReturnsFailureWithoutMerging()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace("unity-git-remote-test");
            var originPath = workspace.CreateBareRepository();
            var seedClonePath = CreateClonedRepository(workspace, originPath, "seed", "base.txt", "base content", "base commit", true);
            var testClonePath = CreateClonedRepository(workspace, originPath, "clone", null, null, null, false);

            UnityGitTestRepository.WriteFile(testClonePath, Path.Combine("Assets", "local.txt"), "local content");
            UnityGitTestRepository.AssertGitSuccess(testClonePath, "add", "--all");
            UnityGitTestRepository.AssertGitSuccess(testClonePath, "commit", "-m", "local commit");
            var localHeadBeforePull = UnityGitTestRepository.ReadGitOutput(testClonePath, "rev-parse", "HEAD");

            UnityGitTestRepository.WriteFile(seedClonePath, Path.Combine("Assets", "remote.txt"), "remote content");
            UnityGitTestRepository.AssertGitSuccess(seedClonePath, "add", "--all");
            UnityGitTestRepository.AssertGitSuccess(seedClonePath, "commit", "-m", "remote commit");
            UnityGitTestRepository.AssertGitSuccess(seedClonePath, "push");

            var refreshCount = 0;
            var originalRefreshAssets = UnityGitRemoteService.RefreshProjectAssets;
            var originalRefreshStatus = UnityGitRemoteService.RefreshStatus;

            try
            {
                UnityGitRemoteService.RefreshProjectAssets = () => refreshCount++;
                UnityGitRemoteService.RefreshStatus = () => refreshCount++;

                var result = UnityGitRemoteService.PullCurrentBranch(testClonePath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Is.Not.Empty);
                Assert.That(result.StandardError, Does.Contain("fast-forward").IgnoreCase);
                Assert.That(refreshCount, Is.EqualTo(0));
                Assert.That(UnityGitTestRepository.ReadGitOutput(testClonePath, "rev-parse", "HEAD"), Is.EqualTo(localHeadBeforePull));
            }
            finally
            {
                UnityGitRemoteService.RefreshProjectAssets = originalRefreshAssets;
                UnityGitRemoteService.RefreshStatus = originalRefreshStatus;
            }
        }

        [Test]
        public void PullCurrentBranch_OutsideRepository_ReturnsFailureWithoutRefresh()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace();
            var refreshCount = 0;
            var originalRefreshAssets = UnityGitRemoteService.RefreshProjectAssets;
            var originalRefreshStatus = UnityGitRemoteService.RefreshStatus;

            try
            {
                UnityGitRemoteService.RefreshProjectAssets = () => refreshCount++;
                UnityGitRemoteService.RefreshStatus = () => refreshCount++;

                var result = UnityGitRemoteService.PullCurrentBranch(workspace.RepositoryPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ShouldRefreshStatus, Is.False);
                Assert.That(result.ErrorMessage, Is.Not.Empty);
                Assert.That(refreshCount, Is.EqualTo(0));
            }
            finally
            {
                UnityGitRemoteService.RefreshProjectAssets = originalRefreshAssets;
                UnityGitRemoteService.RefreshStatus = originalRefreshStatus;
            }
        }

        static string CreateClonedRepository(UnityGitTestRepository workspace, string originPath, string directoryName, string initialFileName, string initialContents, string initialCommitMessage, bool pushInitialCommit)
        {
            var clonePath = workspace.CloneRepository(originPath, directoryName);
            if (!string.IsNullOrWhiteSpace(initialFileName))
            {
                UnityGitTestRepository.WriteFile(clonePath, Path.Combine("Assets", initialFileName), initialContents);
                UnityGitTestRepository.AssertGitSuccess(clonePath, "add", "--all");
                UnityGitTestRepository.AssertGitSuccess(clonePath, "commit", "-m", initialCommitMessage);

                if (pushInitialCommit)
                {
                    UnityGitTestRepository.AssertGitSuccess(clonePath, "push", "-u", "origin", "HEAD");
                }
            }

            return clonePath;
        }
    }
}
