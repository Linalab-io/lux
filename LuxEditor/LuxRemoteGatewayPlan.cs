namespace Linalab.Lux.Editor
{
    public static class LuxRemoteGatewayPlan
    {
        public const string Phase = "Phase 2";
        public const string VideoTransport = "Unity WebRTC video sender";
        public const string SignalingTransport = "WebSocket JSON signaling";
        public const string ControlTransport = "RTCDataChannel structured control RPC";
        public const string PermissionModel = "paired session approval with capability grants and audit log";
        public const bool IncludesIosClientImplementation = false;
    }
}
