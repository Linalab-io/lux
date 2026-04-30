using System;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.IR
{
    [Serializable]
    public sealed class GeneratedAssetManifest
    {
        public const string CurrentSchemaVersion = "0.1";

        public string schemaVersion = CurrentSchemaVersion;
        public GeneratedAssetMetadata metadata = new GeneratedAssetMetadata();
        public GeneratedAssetSourceContext sourceContext = new GeneratedAssetSourceContext();
        public GeneratedAssetImage[] images = Array.Empty<GeneratedAssetImage>();
        public GeneratedAssetLayer[] layers = Array.Empty<GeneratedAssetLayer>();
        public GeneratedAssetMask[] masks = Array.Empty<GeneratedAssetMask>();
        public GeneratedAssetPolygon[] polygons = Array.Empty<GeneratedAssetPolygon>();
        public GeneratedAssetSprite[] sprites = Array.Empty<GeneratedAssetSprite>();
        public GeneratedAssetFrame[] frames = Array.Empty<GeneratedAssetFrame>();
        public GeneratedAssetRigBone[] rigBones = Array.Empty<GeneratedAssetRigBone>();
        public GeneratedAssetSlot[] slots = Array.Empty<GeneratedAssetSlot>();
        public GeneratedAssetAttachment[] attachments = Array.Empty<GeneratedAssetAttachment>();
        public GeneratedAssetMesh[] meshes = Array.Empty<GeneratedAssetMesh>();
        public GeneratedAssetWeight[] weights = Array.Empty<GeneratedAssetWeight>();
        public GeneratedAssetAnimationClip[] animations = Array.Empty<GeneratedAssetAnimationClip>();
        public GeneratedAssetUnity2DExport[] unity2DExports = Array.Empty<GeneratedAssetUnity2DExport>();
        public GeneratedAssetExportHints exportHints = new GeneratedAssetExportHints();
        public GeneratedAssetWarning[] warnings = Array.Empty<GeneratedAssetWarning>();

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public static GeneratedAssetManifest FromJson(string json)
        {
            return JsonUtility.FromJson<GeneratedAssetManifest>(json);
        }

        public static GeneratedAssetManifest CreateDefault()
        {
            return new GeneratedAssetManifest();
        }

        public static GeneratedAssetManifestValidationResult ValidateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return GeneratedAssetManifestValidationResult.InvalidJson("Manifest JSON is empty.");
            }

            if (!JsonContainsField(json, "schemaVersion"))
            {
                return GeneratedAssetManifestValidationResult.MissingSchemaVersion();
            }

            GeneratedAssetManifest manifest;
            try
            {
                manifest = FromJson(json);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return GeneratedAssetManifestValidationResult.InvalidJson(exception.Message);
            }

            return Validate(manifest);
        }

        private static bool JsonContainsField(string json, string fieldName)
        {
            return json.IndexOf('"' + fieldName + '"', StringComparison.Ordinal) >= 0;
        }

        public static GeneratedAssetManifestValidationResult Validate(GeneratedAssetManifest manifest)
        {
            if (manifest == null)
            {
                return GeneratedAssetManifestValidationResult.InvalidJson("Manifest JSON did not produce a manifest.");
            }

            if (string.IsNullOrWhiteSpace(manifest.schemaVersion))
            {
                return GeneratedAssetManifestValidationResult.MissingSchemaVersion();
            }

            if (!string.Equals(manifest.schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                return GeneratedAssetManifestValidationResult.UnsupportedSchemaVersion(manifest.schemaVersion);
            }

            return GeneratedAssetManifestValidationResult.Valid();
        }
    }

    public enum GeneratedAssetManifestValidationStatus
    {
        Valid,
        InvalidJson,
        MissingSchemaVersion,
        UnsupportedSchemaVersion
    }

    public readonly struct GeneratedAssetManifestValidationResult
    {
        public GeneratedAssetManifestValidationResult(GeneratedAssetManifestValidationStatus status, string schemaVersion, string message)
        {
            Status = status;
            SchemaVersion = schemaVersion;
            Message = message;
        }

        public GeneratedAssetManifestValidationStatus Status { get; }
        public string SchemaVersion { get; }
        public string Message { get; }
        public bool IsValid => Status == GeneratedAssetManifestValidationStatus.Valid;

        public static GeneratedAssetManifestValidationResult Valid()
        {
            return new GeneratedAssetManifestValidationResult(
                GeneratedAssetManifestValidationStatus.Valid,
                GeneratedAssetManifest.CurrentSchemaVersion,
                "Manifest schema is supported.");
        }

        public static GeneratedAssetManifestValidationResult InvalidJson(string message)
        {
            return new GeneratedAssetManifestValidationResult(
                GeneratedAssetManifestValidationStatus.InvalidJson,
                string.Empty,
                message);
        }

        public static GeneratedAssetManifestValidationResult MissingSchemaVersion()
        {
            return new GeneratedAssetManifestValidationResult(
                GeneratedAssetManifestValidationStatus.MissingSchemaVersion,
                string.Empty,
                "Manifest schemaVersion is required.");
        }

        public static GeneratedAssetManifestValidationResult UnsupportedSchemaVersion(string schemaVersion)
        {
            return new GeneratedAssetManifestValidationResult(
                GeneratedAssetManifestValidationStatus.UnsupportedSchemaVersion,
                schemaVersion,
                $"Manifest schemaVersion '{schemaVersion}' is not supported.");
        }
    }

    [Serializable]
    public sealed class GeneratedAssetMetadata
    {
        public string assetId;
        public string displayName;
        public string description;
        public string createdUtc;
        public string generatorName;
        public string generatorVersion;
        public string pipelineName;
        public string[] tags = Array.Empty<string>();
        public GeneratedAssetProvenance provenance = new GeneratedAssetProvenance();
    }

    [Serializable]
    public sealed class GeneratedAssetProvenance
    {
        public string prompt;
        public string negativePrompt;
        public string backendName;
        public string backendVersion;
        public string modelName;
        public string seed;
        public string requestId;
        public string sourceManifestPath;
    }

    [Serializable]
    public sealed class GeneratedAssetSourceContext
    {
        public string unityProjectPath;
        public string unityVersion;
        public string scenePath;
        public string selectedAssetPath;
        public string contextJsonPath;
        public string promptTemplatePath;
        public GeneratedAssetReference[] references = Array.Empty<GeneratedAssetReference>();
    }

    [Serializable]
    public sealed class GeneratedAssetReference
    {
        public string id;
        public string kind;
        public string path;
        public string guid;
        public string note;
    }

    [Serializable]
    public sealed class GeneratedAssetImage
    {
        public string id;
        public string role;
        public string path;
        public int width;
        public int height;
        public float pixelsPerUnit = 100f;
        public string colorSpace;
        public string alphaMode;
    }

    [Serializable]
    public sealed class GeneratedAssetLayer
    {
        public string id;
        public string name;
        public string imageId;
        public string parentLayerId;
        public int order;
        public bool visible = true;
        public float opacity = 1f;
        public GeneratedAssetRect rect = new GeneratedAssetRect();
        public string[] maskIds = Array.Empty<string>();
        public string[] polygonIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class GeneratedAssetMask
    {
        public string id;
        public string layerId;
        public string imagePath;
        public string kind;
        public float threshold = 0.5f;
        public GeneratedAssetRect bounds = new GeneratedAssetRect();
        public string polygonId;
    }

    [Serializable]
    public sealed class GeneratedAssetPolygon
    {
        public string id;
        public string maskId;
        public string label;
        public GeneratedAssetVector2[] points = Array.Empty<GeneratedAssetVector2>();
        public float confidence;
    }

    [Serializable]
    public sealed class GeneratedAssetSprite
    {
        public string id;
        public string name;
        public string imageId;
        public string layerId;
        public GeneratedAssetRect rect = new GeneratedAssetRect();
        public GeneratedAssetVector2 pivot = new GeneratedAssetVector2 { x = 0.5f, y = 0.5f };
        public string meshId;
        public string[] attachmentIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class GeneratedAssetFrame
    {
        public string id;
        public string name;
        public string imageId;
        public string spriteId;
        public string path;
        public int index;
        public float durationSeconds;
    }

    [Serializable]
    public sealed class GeneratedAssetRigBone
    {
        public string id;
        public string name;
        public string parentId;
        public float length;
        public GeneratedAssetTransform2D bindPose = new GeneratedAssetTransform2D();
    }

    [Serializable]
    public sealed class GeneratedAssetSlot
    {
        public string id;
        public string name;
        public string boneId;
        public string attachmentId;
        public int drawOrder;
        public string color;
    }

    [Serializable]
    public sealed class GeneratedAssetAttachment
    {
        public string id;
        public string name;
        public string slotId;
        public string spriteId;
        public string meshId;
        public string path;
    }

    [Serializable]
    public sealed class GeneratedAssetMesh
    {
        public string id;
        public string name;
        public GeneratedAssetVector2[] vertices = Array.Empty<GeneratedAssetVector2>();
        public GeneratedAssetVector2[] uvs = Array.Empty<GeneratedAssetVector2>();
        public int[] triangles = Array.Empty<int>();
    }

    [Serializable]
    public sealed class GeneratedAssetWeight
    {
        public string id;
        public string meshId;
        public int vertexIndex;
        public GeneratedAssetBoneInfluence[] influences = Array.Empty<GeneratedAssetBoneInfluence>();
    }

    [Serializable]
    public sealed class GeneratedAssetBoneInfluence
    {
        public string boneId;
        public float weight;
    }

    [Serializable]
    public sealed class GeneratedAssetAnimationClip
    {
        public string id;
        public string name;
        public float frameRate = 12f;
        public bool loop;
        public GeneratedAssetAnimationTrack[] tracks = Array.Empty<GeneratedAssetAnimationTrack>();
    }

    [Serializable]
    public sealed class GeneratedAssetAnimationTrack
    {
        public string id;
        public string targetId;
        public string property;
        public GeneratedAssetKeyframe[] keyframes = Array.Empty<GeneratedAssetKeyframe>();
    }

    [Serializable]
    public sealed class GeneratedAssetKeyframe
    {
        public float timeSeconds;
        public string frameId;
        public GeneratedAssetTransform2D transform = new GeneratedAssetTransform2D();
        public float value;
    }

    [Serializable]
    public sealed class GeneratedAssetExportHints
    {
        public string outputDirectory;
        public string textureFormat;
        public string spritePackingTag;
        public bool generateFrameSequence;
        public bool generateSpriteSheet;
        public bool generateAnimationClip;
        public bool generateUnity2DAnimationDraft;
        public bool generateSpineDraft;
        public string[] labels = Array.Empty<string>();
    }

    [Serializable]
    public sealed class GeneratedAssetUnity2DExport
    {
        public string status;
        public string layeredPngPath;
        public string handoffManifestPath;
        public string draftPrefabPath;
        public string[] boneNames = Array.Empty<string>();
        public string[] missingPackages = Array.Empty<string>();
        public string message;
    }

    [Serializable]
    public sealed class GeneratedAssetWarning
    {
        public string id;
        public string sourceId;
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class GeneratedAssetRect
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    public sealed class GeneratedAssetVector2
    {
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class GeneratedAssetTransform2D
    {
        public GeneratedAssetVector2 position = new GeneratedAssetVector2();
        public float rotationDegrees;
        public GeneratedAssetVector2 scale = new GeneratedAssetVector2 { x = 1f, y = 1f };
    }
}
