using System;
using System.Threading;
using System.Threading.Tasks;
using Linalab.LuxEditor;
using NUnit.Framework;

namespace Linalab.LuxEditor.Tests
{
    public sealed class LuxAIToolDispatcherTests
    {
        [Test]
        public async Task dispatcher_routes_claude_code_command_correctly()
        {
            var runner = new RecordingProcessRunner();
            var dispatcher = CreateDispatcher(runner, new RecordingAiBridgeClient());

            var result = await dispatcher.HandleEventJsonAsync(CreateToolEvent("claude-code", "--print hello"), CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(runner.Executable, Is.EqualTo("claude"));
            Assert.That(runner.Arguments, Is.EqualTo("--print hello"));
        }

        [Test]
        public async Task dispatcher_routes_codex_command_correctly()
        {
            var runner = new RecordingProcessRunner();
            var dispatcher = CreateDispatcher(runner, new RecordingAiBridgeClient());

            var result = await dispatcher.HandleEventJsonAsync(CreateToolEvent("openai-codex", "make a texture"), CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(runner.Executable, Is.EqualTo("codex"));
            Assert.That(runner.Arguments, Is.EqualTo("exec \"make a texture\" -s workspace-write --skip-git-repo-check"));
        }

        [Test]
        public async Task dispatcher_skill_dispatch_maps_to_ai_bridge_command()
        {
            var bridge = new RecordingAiBridgeClient();
            var dispatcher = CreateDispatcher(new RecordingProcessRunner(), bridge);

            var result = await dispatcher.HandleEventJsonAsync(CreateSkillEvent("screenshot", "{\"screenshotCaptureMode\":\"game-view\"}"), CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(bridge.Command, Is.EqualTo("capture_lux_screenshot"));
            Assert.That(bridge.SkillParamsJson, Is.EqualTo("{\"screenshotCaptureMode\":\"game-view\"}"));
        }

        [Test]
        public async Task dispatcher_reports_error_for_unknown_tool()
        {
            var runner = new RecordingProcessRunner();
            var dispatcher = CreateDispatcher(runner, new RecordingAiBridgeClient());
            string reportedError = null;
            dispatcher.OnError += error => reportedError = error;

            var result = await dispatcher.HandleEventJsonAsync(CreateToolEvent("unknown-tool", "hello"), CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("Unknown AI tool type"));
            Assert.That(reportedError, Does.Contain("Unknown AI tool type"));
            Assert.That(runner.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task dispatcher_reports_error_for_unknown_skill()
        {
            var bridge = new RecordingAiBridgeClient();
            var dispatcher = CreateDispatcher(new RecordingProcessRunner(), bridge);
            string reportedError = null;
            dispatcher.OnError += error => reportedError = error;

            var result = await dispatcher.HandleEventJsonAsync(CreateSkillEvent("unknown-skill", "{}"), CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("Unknown Lux skill"));
            Assert.That(reportedError, Does.Contain("Unknown Lux skill"));
            Assert.That(bridge.CallCount, Is.EqualTo(0));
        }

        private static LuxAIToolDispatcher CreateDispatcher(IToolProcessRunner runner, ILuxAiBridgeClient bridge)
        {
            return new LuxAIToolDispatcher(() => new NeverConnectedWebSocketClient(), runner, bridge);
        }

        private static string CreateToolEvent(string toolType, string command)
        {
            return "{\"schema_version\":1,\"event_id\":\"evt\",\"category\":\"tool\",\"source\":\"gateway\",\"session_id\":\"session\",\"payload\":{\"kind\":\"tool-execute\",\"executionId\":\"exec-1\",\"toolType\":\"" + toolType + "\",\"command\":\"" + command + "\"}}";
        }

        private static string CreateSkillEvent(string skillName, string skillParamsJson)
        {
            return "{\"schema_version\":1,\"event_id\":\"evt\",\"category\":\"tool\",\"source\":\"gateway\",\"session_id\":\"session\",\"payload\":{\"kind\":\"skill-dispatch\",\"executionId\":\"exec-2\",\"toolType\":\"opencode\",\"skillName\":\"" + skillName + "\",\"skillParams\":" + skillParamsJson + "}}";
        }

        private sealed class RecordingProcessRunner : IToolProcessRunner
        {
            public string Executable { get; private set; }
            public string Arguments { get; private set; }
            public int CallCount { get; private set; }

            public Task<ToolProcessResult> RunAsync(string executable, string arguments, TimeSpan timeout, CancellationToken cancellationToken, IProgress<string> onOutput = null)
            {
                CallCount++;
                Executable = executable;
                Arguments = arguments;
                onOutput?.Report("progress");
                return Task.FromResult(new ToolProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "ok",
                    StandardError = string.Empty
                });
            }
        }

        private sealed class RecordingAiBridgeClient : ILuxAiBridgeClient
        {
            public string Command { get; private set; }
            public string SkillParamsJson { get; private set; }
            public int CallCount { get; private set; }

            public Task<string> SendCommandAsync(string command, string skillParamsJson, CancellationToken cancellationToken)
            {
                CallCount++;
                Command = command;
                SkillParamsJson = skillParamsJson;
                return Task.FromResult("{\"ok\":true}");
            }
        }

        private sealed class NeverConnectedWebSocketClient : ILuxAIToolDispatcherWebSocketClient
        {
            public bool IsConnected => false;

            public Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<string>(null);
            }

            public Task SendTextAsync(string message, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
