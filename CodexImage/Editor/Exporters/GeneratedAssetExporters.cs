using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Linalab.UnityCodexImage.Editor.IR;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Exporters
{
    public sealed class GeneratedAssetFrameSequenceExportOptions
    {
        public string outputDirectory;
        public string fileNamePrefix = "frame";
        public bool overwriteExisting;
        public bool importAsSprites = true;
    }

    public sealed class GeneratedAssetFrameSequenceExportResult
    {
        public bool succeeded;
        public string message;
        public string[] framePaths = Array.Empty<string>();
        public Sprite[] importedSprites = Array.Empty<Sprite>();
    }

    public sealed class GeneratedAssetSpritesheetExportOptions
    {
        public string outputPath;
        public string spriteNamePrefix = "frame";
        public int columns;
        public bool overwriteExisting;
        public bool importAsMultipleSprites = true;
    }

    public sealed class GeneratedAssetSpritesheetExportResult
    {
        public bool succeeded;
        public string message;
        public string spritesheetPath;
        public string[] spriteNames = Array.Empty<string>();
        public Sprite[] importedSprites = Array.Empty<Sprite>();
        public Rect[] spriteRects = Array.Empty<Rect>();
    }

    public sealed class GeneratedAssetAnimationClipExportOptions
    {
        public string outputPath;
        public string clipName;
        public float frameRate;
        public bool? loop;
        public bool overwriteExisting;
    }

    public sealed class GeneratedAssetAnimationClipExportResult
    {
        public bool succeeded;
        public string message;
        public string clipPath;
        public AnimationClip clip;
    }

    public static class GeneratedAssetFrameSequenceExporter
    {
        public static GeneratedAssetFrameSequenceExportResult Export(GeneratedAssetManifest manifest, GeneratedAssetFrameSequenceExportOptions options)
        {
            try
            {
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                if (string.IsNullOrWhiteSpace(options.outputDirectory))
                {
                    throw new ArgumentException("Frame sequence export requires an output directory.");
                }

                var frames = GeneratedAssetExportFrameResolver.Resolve(manifest);
                Directory.CreateDirectory(options.outputDirectory);
                var paths = new List<string>(frames.Length);
                var importedSprites = new List<Sprite>(frames.Length);
                var prefix = string.IsNullOrWhiteSpace(options.fileNamePrefix) ? "frame" : GeneratedAssetExportFrameResolver.SanitizeName(options.fileNamePrefix);

                for (var index = 0; index < frames.Length; index++)
                {
                    var frame = frames[index];
                    var path = GeneratedAssetExportFrameResolver.NormalizePath(Path.Combine(
                        options.outputDirectory,
                        prefix + "_" + index.ToString("000", CultureInfo.InvariantCulture) + ".png"));
                    GeneratedAssetExportFrameResolver.EnsureSafeWrite(path, options.overwriteExisting);
                    GeneratedAssetExportFrameResolver.WriteTexture(path, frame.texture);
                    paths.Add(path);

                    if (options.importAsSprites)
                    {
                        var sprite = GeneratedAssetExportFrameResolver.ImportSingleSprite(path, frame.pixelsPerUnit, frame.pivot);
                        if (sprite != null)
                        {
                            importedSprites.Add(sprite);
                        }
                    }
                }

                return new GeneratedAssetFrameSequenceExportResult
                {
                    succeeded = true,
                    message = "Frame sequence export completed.",
                    framePaths = paths.ToArray(),
                    importedSprites = importedSprites.ToArray()
                };
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException || exception is UnauthorizedAccessException)
            {
                return new GeneratedAssetFrameSequenceExportResult
                {
                    succeeded = false,
                    message = exception.Message
                };
            }
        }
    }

    public static class GeneratedAssetSpritesheetExporter
    {
        public static GeneratedAssetSpritesheetExportResult Export(GeneratedAssetManifest manifest, GeneratedAssetSpritesheetExportOptions options)
        {
            try
            {
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                if (string.IsNullOrWhiteSpace(options.outputPath))
                {
                    throw new ArgumentException("Spritesheet export requires an output path.");
                }

                var frames = GeneratedAssetExportFrameResolver.Resolve(manifest);
                GeneratedAssetExportFrameResolver.EnsureSafeWrite(options.outputPath, options.overwriteExisting);

                var cellWidth = frames.Max(frame => frame.texture.width);
                var cellHeight = frames.Max(frame => frame.texture.height);
                var columns = options.columns > 0 ? options.columns : Mathf.CeilToInt(Mathf.Sqrt(frames.Length));
                columns = Mathf.Max(1, columns);
                var rows = Mathf.CeilToInt(frames.Length / (float)columns);
                var sheet = new Texture2D(columns * cellWidth, rows * cellHeight, TextureFormat.RGBA32, false);
                var transparent = Enumerable.Repeat(new Color32(0, 0, 0, 0), sheet.width * sheet.height).ToArray();
                sheet.SetPixels32(transparent);

                var names = new string[frames.Length];
                var rects = new Rect[frames.Length];
                var prefix = string.IsNullOrWhiteSpace(options.spriteNamePrefix) ? "frame" : GeneratedAssetExportFrameResolver.SanitizeName(options.spriteNamePrefix);
                for (var index = 0; index < frames.Length; index++)
                {
                    var frame = frames[index];
                    var column = index % columns;
                    var row = index / columns;
                    var x = column * cellWidth;
                    var y = (rows - row - 1) * cellHeight;
                    sheet.SetPixels32(x, y, frame.texture.width, frame.texture.height, frame.texture.GetPixels32());
                    names[index] = prefix + "_" + index.ToString("000", CultureInfo.InvariantCulture);
                    rects[index] = new Rect(x, y, frame.texture.width, frame.texture.height);
                }

                sheet.Apply();
                GeneratedAssetExportFrameResolver.WriteTexture(options.outputPath, sheet);
                var importedSprites = options.importAsMultipleSprites
                    ? GeneratedAssetExportFrameResolver.ImportMultipleSprites(options.outputPath, names, rects, frames)
                    : Array.Empty<Sprite>();

                return new GeneratedAssetSpritesheetExportResult
                {
                    succeeded = true,
                    message = "Spritesheet export completed.",
                    spritesheetPath = GeneratedAssetExportFrameResolver.NormalizePath(options.outputPath),
                    spriteNames = names,
                    spriteRects = rects,
                    importedSprites = importedSprites
                };
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException || exception is UnauthorizedAccessException)
            {
                return new GeneratedAssetSpritesheetExportResult
                {
                    succeeded = false,
                    message = exception.Message
                };
            }
        }
    }

    public static class GeneratedAssetAnimationClipExporter
    {
        public static GeneratedAssetAnimationClipExportResult Export(GeneratedAssetManifest manifest, IReadOnlyList<Sprite> frameSprites, GeneratedAssetAnimationClipExportOptions options)
        {
            try
            {
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                if (string.IsNullOrWhiteSpace(options.outputPath))
                {
                    throw new ArgumentException("AnimationClip export requires an output path.");
                }

                var frames = GeneratedAssetExportFrameResolver.Resolve(manifest, loadTextures: false);
                if (frameSprites == null || frameSprites.Count != frames.Length || frameSprites.Any(sprite => sprite == null))
                {
                    throw new ArgumentException("AnimationClip export requires one non-null Sprite per IR frame.");
                }

                GeneratedAssetExportFrameResolver.EnsureSafeWrite(options.outputPath, options.overwriteExisting);
                var animation = GeneratedAssetExportFrameResolver.ResolveAnimation(manifest);
                var frameRate = options.frameRate > 0f ? options.frameRate : animation.frameRate;
                frameRate = frameRate > 0f ? frameRate : GeneratedAssetExportFrameResolver.ResolveFallbackFrameRate(frames);
                var clip = new AnimationClip
                {
                    name = string.IsNullOrWhiteSpace(options.clipName) ? GeneratedAssetExportFrameResolver.ResolveClipName(animation) : options.clipName,
                    frameRate = frameRate
                };
                var keyframes = new ObjectReferenceKeyframe[frames.Length];
                for (var index = 0; index < frames.Length; index++)
                {
                    keyframes[index] = new ObjectReferenceKeyframe
                    {
                        time = frames[index].timeSeconds,
                        value = frameSprites[index]
                    };
                }

                var binding = new EditorCurveBinding
                {
                    path = string.Empty,
                    type = typeof(SpriteRenderer),
                    propertyName = "m_Sprite"
                };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = options.loop ?? animation.loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                var projectPath = GeneratedAssetExportFrameResolver.ProjectRelativePath(options.outputPath);
                var directory = Path.GetDirectoryName(options.outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!string.IsNullOrWhiteSpace(projectPath))
                {
                    AssetDatabase.CreateAsset(clip, projectPath);
                    AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
                    clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(projectPath) ?? clip;
                }
                else
                {
                    throw new ArgumentException("AnimationClip output path must be inside the Unity project.");
                }

                return new GeneratedAssetAnimationClipExportResult
                {
                    succeeded = true,
                    message = "AnimationClip export completed.",
                    clipPath = GeneratedAssetExportFrameResolver.NormalizePath(options.outputPath),
                    clip = clip
                };
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException || exception is UnauthorizedAccessException)
            {
                return new GeneratedAssetAnimationClipExportResult
                {
                    succeeded = false,
                    message = exception.Message
                };
            }
        }
    }

    internal static class GeneratedAssetExportFrameResolver
    {
        internal static ResolvedGeneratedAssetFrame[] Resolve(GeneratedAssetManifest manifest, bool loadTextures = true)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var framesById = (manifest.frames ?? Array.Empty<GeneratedAssetFrame>())
                .Where(frame => frame != null)
                .ToDictionary(frame => frame.id ?? string.Empty, StringComparer.Ordinal);
            if (framesById.Count == 0)
            {
                throw new ArgumentException("Generated asset manifest does not contain animation frames.");
            }

            var orderedFrames = ResolveOrderedFrames(manifest, framesById, out var keyframeTimes);
            if (orderedFrames.Length == 0)
            {
                throw new ArgumentException("Generated asset manifest does not contain animation frames.");
            }

            var imagesById = (manifest.images ?? Array.Empty<GeneratedAssetImage>())
                .Where(image => image != null && !string.IsNullOrWhiteSpace(image.id))
                .ToDictionary(image => image.id, StringComparer.Ordinal);
            var spritesById = (manifest.sprites ?? Array.Empty<GeneratedAssetSprite>())
                .Where(sprite => sprite != null && !string.IsNullOrWhiteSpace(sprite.id))
                .ToDictionary(sprite => sprite.id, StringComparer.Ordinal);
            var result = new ResolvedGeneratedAssetFrame[orderedFrames.Length];
            var cumulativeTime = 0f;
            var fallbackFrameDuration = 1f / ResolveFallbackFrameRate(manifest);

            for (var index = 0; index < orderedFrames.Length; index++)
            {
                var frame = orderedFrames[index];
                spritesById.TryGetValue(frame.spriteId ?? string.Empty, out var sprite);
                var image = ResolveImage(frame, sprite, imagesById);
                var timeSeconds = keyframeTimes != null && index < keyframeTimes.Length ? keyframeTimes[index] : cumulativeTime;
                result[index] = new ResolvedGeneratedAssetFrame
                {
                    frame = frame,
                    sourceImage = image,
                    sourceSprite = sprite,
                    timeSeconds = timeSeconds,
                    pivot = ResolvePivot(sprite),
                    pixelsPerUnit = image != null && image.pixelsPerUnit > 0f ? image.pixelsPerUnit : 100f,
                    texture = loadTextures ? LoadFrameTexture(frame, sprite, image) : null
                };
                cumulativeTime += frame.durationSeconds > 0f ? frame.durationSeconds : fallbackFrameDuration;
            }

            return result;
        }

        internal static GeneratedAssetAnimationClip ResolveAnimation(GeneratedAssetManifest manifest)
        {
            return (manifest.animations ?? Array.Empty<GeneratedAssetAnimationClip>()).FirstOrDefault(animation => animation != null)
                ?? new GeneratedAssetAnimationClip { name = "generated_animation", frameRate = ResolveFallbackFrameRate(manifest) };
        }

        internal static string ResolveClipName(GeneratedAssetAnimationClip animation)
        {
            return string.IsNullOrWhiteSpace(animation.name) ? "generated_animation" : SanitizeName(animation.name);
        }

        internal static float ResolveFallbackFrameRate(ResolvedGeneratedAssetFrame[] frames)
        {
            var positiveDuration = frames.Select(frame => frame.frame.durationSeconds).FirstOrDefault(duration => duration > 0f);
            return positiveDuration > 0f ? 1f / positiveDuration : 12f;
        }

        internal static void EnsureSafeWrite(string path, bool overwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Export path is required.");
            }

            var normalized = NormalizePath(path);
            var projectPath = ProjectRelativePath(normalized);
            var existingAsset = string.IsNullOrWhiteSpace(projectPath) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectPath);
            if (!overwriteExisting && (File.Exists(normalized) || existingAsset != null))
            {
                throw new InvalidOperationException("Refusing to overwrite existing export path: " + normalized);
            }

            if (overwriteExisting)
            {
                if (!string.IsNullOrWhiteSpace(projectPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectPath) != null)
                {
                    AssetDatabase.DeleteAsset(projectPath);
                }
                else if (File.Exists(normalized))
                {
                    File.Delete(normalized);
                }
            }
        }

        internal static void WriteTexture(string path, Texture2D texture)
        {
            var normalized = NormalizePath(path);
            var directory = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(normalized, texture.EncodeToPNG());
        }

        internal static Sprite ImportSingleSprite(string path, float pixelsPerUnit, Vector2 pivot)
        {
            var projectPath = ProjectRelativePath(path);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(projectPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.spritePixelsPerUnit = pixelsPerUnit > 0f ? pixelsPerUnit : 100f;
                importer.spritePivot = pivot;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(projectPath);
        }

        internal static Sprite[] ImportMultipleSprites(string path, string[] names, Rect[] rects, ResolvedGeneratedAssetFrame[] frames)
        {
            var projectPath = ProjectRelativePath(path);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return Array.Empty<Sprite>();
            }

            AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(projectPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.spritePixelsPerUnit = frames.Length > 0 && frames[0].pixelsPerUnit > 0f ? frames[0].pixelsPerUnit : 100f;
                var metadata = new SpriteMetaData[names.Length];
                for (var index = 0; index < names.Length; index++)
                {
                    metadata[index] = new SpriteMetaData
                    {
                        name = names[index],
                        rect = rects[index],
                        pivot = frames[index].pivot,
                        alignment = (int)SpriteAlignment.Custom
                    };
                }

#pragma warning disable CS0618
                importer.spritesheet = metadata;
#pragma warning restore CS0618
                importer.SaveAndReimport();
            }

            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(projectPath).OfType<Sprite>().ToArray();
            return names.Select(name => sprites.FirstOrDefault(sprite => string.Equals(sprite.name, name, StringComparison.Ordinal))).ToArray();
        }

        internal static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        internal static string ProjectRelativePath(string path)
        {
            var normalized = NormalizePath(path);
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return normalized;
            }

            var projectRoot = NormalizePath(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            var fullPath = NormalizePath(Path.GetFullPath(normalized));
            if (!fullPath.StartsWith(projectRoot + "/", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return fullPath.Substring(projectRoot.Length + 1);
        }

        internal static string SanitizeName(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "frame" : value.Trim().ToLowerInvariant();
            var builder = new StringBuilder(normalized.Length);
            var previousSeparator = false;
            foreach (var character in normalized)
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                    previousSeparator = false;
                }
                else if (!previousSeparator)
                {
                    builder.Append('_');
                    previousSeparator = true;
                }
            }

            var result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "frame" : result;
        }

        private static GeneratedAssetFrame[] ResolveOrderedFrames(GeneratedAssetManifest manifest, IReadOnlyDictionary<string, GeneratedAssetFrame> framesById, out float[] keyframeTimes)
        {
            var track = (manifest.animations ?? Array.Empty<GeneratedAssetAnimationClip>())
                .Where(animation => animation != null)
                .SelectMany(animation => animation.tracks ?? Array.Empty<GeneratedAssetAnimationTrack>())
                .FirstOrDefault(candidate => candidate != null && (candidate.keyframes ?? Array.Empty<GeneratedAssetKeyframe>()).Any(keyframe => keyframe != null && !string.IsNullOrWhiteSpace(keyframe.frameId)));
            if (track != null)
            {
                var pairs = (track.keyframes ?? Array.Empty<GeneratedAssetKeyframe>())
                    .Where(keyframe => keyframe != null && framesById.ContainsKey(keyframe.frameId))
                    .OrderBy(keyframe => keyframe.timeSeconds)
                    .Select(keyframe => new KeyValuePair<GeneratedAssetFrame, float>(framesById[keyframe.frameId], keyframe.timeSeconds))
                    .ToArray();
                if (pairs.Length > 0)
                {
                    keyframeTimes = pairs.Select(pair => pair.Value).ToArray();
                    return pairs.Select(pair => pair.Key).ToArray();
                }
            }

            keyframeTimes = null;
            return framesById.Values
                .OrderBy(frame => frame.index)
                .ThenBy(frame => frame.id, StringComparer.Ordinal)
                .ToArray();
        }

        private static GeneratedAssetImage ResolveImage(GeneratedAssetFrame frame, GeneratedAssetSprite sprite, IReadOnlyDictionary<string, GeneratedAssetImage> imagesById)
        {
            if (!string.IsNullOrWhiteSpace(frame.imageId) && imagesById.TryGetValue(frame.imageId, out var frameImage))
            {
                return frameImage;
            }

            if (sprite != null && !string.IsNullOrWhiteSpace(sprite.imageId) && imagesById.TryGetValue(sprite.imageId, out var spriteImage))
            {
                return spriteImage;
            }

            return null;
        }

        private static Texture2D LoadFrameTexture(GeneratedAssetFrame frame, GeneratedAssetSprite sprite, GeneratedAssetImage image)
        {
            var path = !string.IsNullOrWhiteSpace(frame.path) ? frame.path : image?.path;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new InvalidOperationException("Frame image path does not exist: " + (path ?? string.Empty));
            }

            var source = LoadTexture(path);
            if (sprite == null || sprite.rect == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
            {
                return source;
            }

            var x = Mathf.Clamp(Mathf.RoundToInt(sprite.rect.x), 0, source.width - 1);
            var y = Mathf.Clamp(Mathf.RoundToInt(sprite.rect.y), 0, source.height - 1);
            var width = Mathf.Clamp(Mathf.RoundToInt(sprite.rect.width), 1, source.width - x);
            var height = Mathf.Clamp(Mathf.RoundToInt(sprite.rect.height), 1, source.height - y);
            var cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var sourcePixels = source.GetPixels32();
            var croppedPixels = new Color32[width * height];
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    croppedPixels[row * width + column] = sourcePixels[(y + row) * source.width + x + column];
                }
            }

            cropped.SetPixels32(croppedPixels);
            cropped.Apply();
            return cropped;
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                throw new InvalidOperationException("PNG could not be decoded: " + path);
            }

            return texture;
        }

        private static Vector2 ResolvePivot(GeneratedAssetSprite sprite)
        {
            if (sprite?.pivot == null)
            {
                return new Vector2(0.5f, 0.5f);
            }

            return new Vector2(Mathf.Clamp01(sprite.pivot.x), Mathf.Clamp01(sprite.pivot.y));
        }

        private static float ResolveFallbackFrameRate(GeneratedAssetManifest manifest)
        {
            var animationFrameRate = (manifest.animations ?? Array.Empty<GeneratedAssetAnimationClip>())
                .Where(animation => animation != null)
                .Select(animation => animation.frameRate)
                .FirstOrDefault(frameRate => frameRate > 0f);
            if (animationFrameRate > 0f)
            {
                return animationFrameRate;
            }

            var frameDuration = (manifest.frames ?? Array.Empty<GeneratedAssetFrame>())
                .Where(frame => frame != null)
                .Select(frame => frame.durationSeconds)
                .FirstOrDefault(duration => duration > 0f);
            return frameDuration > 0f ? 1f / frameDuration : 12f;
        }
    }

    internal sealed class ResolvedGeneratedAssetFrame
    {
        public GeneratedAssetFrame frame;
        public GeneratedAssetImage sourceImage;
        public GeneratedAssetSprite sourceSprite;
        public Texture2D texture;
        public float timeSeconds;
        public float pixelsPerUnit;
        public Vector2 pivot;
    }
}
