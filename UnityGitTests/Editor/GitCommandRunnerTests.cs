using Linalab.UnityGit.Editor;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;

namespace Linalab.UnityGit.Editor.Tests
{
    public class GitCommandRunnerTests
    {
        [Test]
        public void Execute_RunsStatusShortInTemporaryRepository()
        {
            using var repository = UnityGitTestRepository.Create();

            File.WriteAllText(repository.GetPath("utf8-파일.txt"), "changed");

            var result = GitCommandRunner.Execute(repository.RepositoryPath, "status", "--short");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.WorkingDirectory, Is.EqualTo(repository.RepositoryPath));
            Assert.That(result.StandardOutput, Does.Contain("?? utf8-파일.txt"));
            Assert.That(result.Output, Is.EqualTo(result.StandardOutput));
        }

        [Test]
        public async Task ExecuteSerializedAsync_WithConcurrentRequests_CompletesThroughControlledQueue()
        {
            using var repository = UnityGitTestRepository.Create();

            File.WriteAllText(repository.GetPath("queued.txt"), "changed");

            var first = GitCommandRunner.ExecuteSerializedAsync(repository.RepositoryPath, "status", "--short");
            var second = GitCommandRunner.ExecuteSerializedAsync(repository.RepositoryPath, "status", "--short");
            var third = GitCommandRunner.ExecuteSerializedAsync(repository.RepositoryPath, "status", "--short");

            var results = await Task.WhenAll(first, second, third);

            Assert.That(results, Has.Length.EqualTo(3));
            foreach (var result in results)
            {
                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.ExitCode, Is.EqualTo(0));
                Assert.That(result.WorkingDirectory, Is.EqualTo(repository.RepositoryPath));
                Assert.That(result.StandardOutput, Does.Contain("?? queued.txt"));
            }
        }
    }
}
