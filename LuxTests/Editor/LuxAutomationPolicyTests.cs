using NUnit.Framework;
using Linalab.UnityAiBridge.Editor;

namespace Linalab.Lux.Editor.Tests
{
    public sealed class LuxAutomationPolicyTests
    {
        [Test]
        public void Evaluate_AllowsBroadReadCommand()
        {
            var policy = new LuxAutomationPolicy();

            var decision = policy.Evaluate("git status --short");

            Assert.That(decision.Kind, Is.EqualTo(LuxAutomationDecisionKind.Allow));
        }

        [Test]
        public void Evaluate_BlocksDangerousCommand()
        {
            var policy = new LuxAutomationPolicy();

            var decision = policy.Evaluate("git reset --hard HEAD");

            Assert.That(decision.Kind, Is.EqualTo(LuxAutomationDecisionKind.Block));
        }

        [Test]
        public void ExecuteShellCommand_RecordsDeniedAuditEntry()
        {
            var audit = new LuxAutomationAuditLog();
            var gateway = new LuxAutomationGateway(new LuxAutomationPolicy(), audit);

            var result = gateway.ExecuteShellCommand("sudo rm -rf /tmp/lux-test", "/tmp");

            Assert.That(result.Allowed, Is.False);
            Assert.That(audit.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void AiBridge_GetLuxContext_ReturnsRemoteAndAutomationSurface()
        {
            LuxAiBridgeProtocolRegistration.RegisterCommands();

            var response = UnityAiBridgeProtocol.Handle(
                new UnityAiBridgeProtocolRequest
                {
                    schemaVersion = UnityAiBridgeProtocol.SchemaVersion,
                    requestId = "req-lux-context",
                    command = "get_lux_context",
                    token = "token-123",
                    @params = new UnityAiBridgeProtocolRequestParameters()
                },
                "token-123");

            Assert.That(response.ok, Is.True);
            Assert.That(response.payload.luxContext, Is.Not.Null);
            Assert.That(response.payload.luxContext.packageName, Is.EqualTo("com.linalab.lux"));
            Assert.That(response.payload.luxContext.controlTransport, Is.EqualTo(LuxAutomationGateway.RemoteAddonUnavailable));
            Assert.That(response.payload.luxContext.automationBlockedTokens, Does.Contain("sudo"));
        }

        [Test]
        public void AiBridge_ExecuteLuxShell_ReturnsPolicyDeniedResultWithoutExecuting()
        {
            LuxAiBridgeProtocolRegistration.RegisterCommands();

            var response = UnityAiBridgeProtocol.Handle(
                new UnityAiBridgeProtocolRequest
                {
                    schemaVersion = UnityAiBridgeProtocol.SchemaVersion,
                    requestId = "req-lux-denied-shell",
                    command = "execute_lux_shell",
                    token = "token-123",
                    @params = new UnityAiBridgeProtocolRequestParameters
                    {
                        commandText = "sudo rm -rf /tmp/lux-test",
                        workingDirectory = "/tmp",
                        actor = "test-agent"
                    }
                },
                "token-123");

            var result = response.payload.luxAutomationResult;

            Assert.That(response.ok, Is.True);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.allowed, Is.False);
            Assert.That(result.success, Is.False);
            Assert.That(result.message, Does.Contain("blocked token"));
        }
    }
}
