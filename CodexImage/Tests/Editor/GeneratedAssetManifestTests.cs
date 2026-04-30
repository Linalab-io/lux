using System;
using System.IO;
using System.Linq;
using Linalab.UnityCodexImage.Editor.IR;
using NUnit.Framework;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class GeneratedAssetManifestTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string ValidFixturePath = PackageRoot + "/Tests/Editor/Fixtures/IR/generated-asset-manifest-valid.json";
        private const string UnsupportedSchemaFixturePath = PackageRoot + "/Tests/Editor/Fixtures/IR/generated-asset-manifest-unsupported-schema.json";
        private const string MissingSchemaFixturePath = PackageRoot + "/Tests/Editor/Fixtures/IR/generated-asset-manifest-missing-schema.json";
        private const string GoldenMismatchEvidencePath = PackageRoot + "/.sisyphus/evidence/task-11-golden-mismatch.txt";
        private const string RoundtripEvidencePath = PackageRoot + "/.sisyphus/evidence/task-1-ir-roundtrip.txt";
        private const string InvalidSchemaEvidencePath = PackageRoot + "/.sisyphus/evidence/task-1-ir-invalid-schema.txt";

        [Test]
        public void DefaultManifestUsesCanonicalSchemaVersion()
        {
            var manifest = GeneratedAssetManifest.CreateDefault();

            Assert.That(manifest.schemaVersion, Is.EqualTo("0.1"));
            Assert.That(GeneratedAssetManifest.Validate(manifest).IsValid, Is.True);
        }

        [Test]
        public void RoundTripsFixtureManifest()
        {
            var json = File.ReadAllText(ValidFixturePath);
            var validation = GeneratedAssetManifest.ValidateJson(json);

            Assert.That(validation.Status, Is.EqualTo(GeneratedAssetManifestValidationStatus.Valid), validation.Message);

            var manifest = GeneratedAssetManifest.FromJson(json);
            Assert.That(manifest.schemaVersion, Is.EqualTo(GeneratedAssetManifest.CurrentSchemaVersion));
            Assert.That(manifest.images, Has.Length.EqualTo(1));
            Assert.That(manifest.layers, Has.Length.EqualTo(3));
            Assert.That(manifest.masks, Has.Length.EqualTo(3));
            Assert.That(manifest.polygons, Has.Length.EqualTo(3));
            Assert.That(manifest.rigBones, Has.Length.EqualTo(5));
            Assert.That(manifest.slots, Has.Length.EqualTo(3));
            Assert.That(manifest.frames, Has.Length.EqualTo(4));
            Assert.That(manifest.animations, Has.Length.EqualTo(1));

            var roundTripped = GeneratedAssetManifest.FromJson(manifest.ToJson());
            Assert.That(roundTripped.metadata.assetId, Is.EqualTo(manifest.metadata.assetId));
            Assert.That(roundTripped.layers.Select(layer => layer.id), Is.EqualTo(manifest.layers.Select(layer => layer.id)));
            Assert.That(roundTripped.rigBones.Select(bone => bone.name), Is.EqualTo(manifest.rigBones.Select(bone => bone.name)));
            Assert.That(roundTripped.animations[0].tracks[0].keyframes, Has.Length.EqualTo(4));

            WriteEvidence(
                RoundtripEvidencePath,
                "GeneratedAssetManifest roundtrip passed",
                $"schemaVersion={roundTripped.schemaVersion}",
                $"images={roundTripped.images.Length}",
                $"layers={roundTripped.layers.Length}",
                $"masks={roundTripped.masks.Length}",
                $"polygons={roundTripped.polygons.Length}",
                $"rigBones={roundTripped.rigBones.Length}",
                $"slots={roundTripped.slots.Length}",
                $"frames={roundTripped.frames.Length}",
                $"animations={roundTripped.animations.Length}");
        }

        [Test]
        public void GoldenFixtureStillContainsRequiredSchemaFields()
        {
            var json = File.ReadAllText(ValidFixturePath);
            var manifest = GeneratedAssetManifest.FromJson(json);
            var requiredFields = new[]
            {
                "\"schemaVersion\"",
                "\"metadata\"",
                "\"sourceContext\"",
                "\"images\"",
                "\"layers\"",
                "\"masks\"",
                "\"polygons\"",
                "\"sprites\"",
                "\"frames\"",
                "\"rigBones\"",
                "\"slots\"",
                "\"attachments\"",
                "\"meshes\"",
                "\"weights\"",
                "\"animations\"",
                "\"exportHints\""
            };

            var missingFields = requiredFields.Where(field => !json.Contains(field, StringComparison.Ordinal)).ToArray();

            Assert.That(missingFields, Is.Empty);
            Assert.That(GeneratedAssetManifest.ValidateJson(json).IsValid, Is.True);
            Assert.That(manifest.schemaVersion, Is.EqualTo(GeneratedAssetManifest.CurrentSchemaVersion));
            Assert.That(manifest.exportHints.generateFrameSequence, Is.True);
            Assert.That(manifest.exportHints.generateSpriteSheet, Is.True);
            Assert.That(manifest.exportHints.generateAnimationClip, Is.True);
            Assert.That(manifest.exportHints.generateUnity2DAnimationDraft, Is.True);
            Assert.That(manifest.exportHints.generateSpineDraft, Is.True);

            WriteEvidence(
                GoldenMismatchEvidencePath,
                "Task 11 golden manifest comparison passed",
                "fixture=" + ValidFixturePath,
                "schemaVersion=" + manifest.schemaVersion,
                "requiredFields=" + string.Join(",", requiredFields.Select(field => field.Trim('"'))),
                "images=" + manifest.images.Length,
                "layers=" + manifest.layers.Length,
                "frames=" + manifest.frames.Length,
                "animations=" + manifest.animations.Length);
        }

        [Test]
        public void UnsupportedSchemaReturnsStructuredValidationResult()
        {
            var json = File.ReadAllText(UnsupportedSchemaFixturePath);
            var result = GeneratedAssetManifest.ValidateJson(json);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Status, Is.EqualTo(GeneratedAssetManifestValidationStatus.UnsupportedSchemaVersion));
            Assert.That(result.SchemaVersion, Is.EqualTo("999"));
            Assert.That(result.Message, Does.Contain("999"));

            WriteEvidence(
                InvalidSchemaEvidencePath,
                "GeneratedAssetManifest unsupported schema validation passed",
                $"status={result.Status}",
                $"schemaVersion={result.SchemaVersion}",
                $"message={result.Message}");
        }

        [Test]
        public void InvalidJsonReturnsStructuredValidationResult()
        {
            var result = GeneratedAssetManifest.ValidateJson("{ not-json }");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Status, Is.EqualTo(GeneratedAssetManifestValidationStatus.InvalidJson));
            Assert.That(result.Message, Is.Not.Empty);
        }

        [Test]
        public void MissingSchemaVersionFixtureReturnsStructuredValidationResult()
        {
            var json = File.ReadAllText(MissingSchemaFixturePath);
            var result = GeneratedAssetManifest.ValidateJson(json);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Status, Is.EqualTo(GeneratedAssetManifestValidationStatus.MissingSchemaVersion));
            Assert.That(result.Message, Does.Contain("schemaVersion"));
        }

        [Test]
        public void IrModelTypesStayNamespaceIndependent()
        {
            var forbiddenNames = new[]
            {
                "Spine.",
                "UnityEditor.U2D.Animation",
                "UnityEngine.U2D.Animation",
                "UnityEditor.U2D.PSD"
            };

            var modelTypes = typeof(GeneratedAssetManifest).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(GeneratedAssetManifest).Namespace)
                .ToArray();

            Assert.That(modelTypes, Is.Not.Empty);
            foreach (var type in modelTypes)
            {
                Assert.That(forbiddenNames.Any(type.FullName.Contains), Is.False, type.FullName);
            }

            var referencedAssemblies = typeof(GeneratedAssetManifest).Assembly.GetReferencedAssemblies()
                .Select(assemblyName => assemblyName.Name)
                .ToArray();

            Assert.That(referencedAssemblies.Any(name => name.Contains("Spine")), Is.False);
            Assert.That(referencedAssemblies.Any(name => name.Contains("2D.Animation")), Is.False);
            Assert.That(referencedAssemblies.Any(name => name.Contains("PSD")), Is.False);
        }

        private static void WriteEvidence(string path, params string[] lines)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, lines.Concat(new[] { $"utc={DateTime.UtcNow:O}" }));
        }
    }
}
