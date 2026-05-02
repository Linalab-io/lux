using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using Linalab.UnityAiBridge.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputMouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

namespace Linalab.Lux.Editor
{
    public static partial class LuxAiBridgeProtocolRegistration
    {
        static UnityAiBridgeProtocolResponse GetHierarchy(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var filters = CreateHierarchyFilters(parameters);

            try
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return UnityAiBridgeProtocol.CreateErrorResponse(
                        request.requestId,
                        UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                        "No active loaded scene is available for hierarchy export.");
                }

                var roots = CollectHierarchyRoots(activeScene, filters);

                int nodeCount = 0;
                var nodeList = new List<UnityAiBridgeHierarchyNode>();
                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    nodeList.Add(BuildHierarchyNode(root, ref nodeCount));
                }

                var safeRequestId = string.IsNullOrWhiteSpace(request.requestId)
                    ? Guid.NewGuid().ToString("N")
                    : request.requestId;
                var outputDirectory = LuxArtifactPaths.GetPersistentOutputPath("hierarchy");
                var outputPath = Path.Combine(outputDirectory, safeRequestId + ".json");

                var activeScenePayload = new UnityAiBridgeHierarchyActiveScene
                {
                    name = activeScene.name ?? string.Empty,
                    path = activeScene.path ?? string.Empty
                };
                var export = new UnityAiBridgeHierarchyExport
                {
                    schemaVersion = UnityAiBridgeProtocol.SchemaVersion,
                    requestId = safeRequestId,
                    rootCount = nodeList.Count,
                    nodeCount = nodeCount,
                    activeScene = activeScenePayload,
                    filters = filters,
                    roots = nodeList.ToArray()
                };

                try
                {
                    File.WriteAllText(outputPath, JsonUtility.ToJson(export, true));
                }
                catch (Exception exception)
                {
                    return UnityAiBridgeProtocol.CreateErrorResponse(
                        request.requestId,
                        UnityAiBridgeProtocol.ErrorCodeArtifactWriteFailed,
                        $"Failed to write hierarchy export: {exception.Message}");
                }

                var metadata = LuxArtifactPaths.WriteArtifactMetadata(outputPath, "hierarchy", safeRequestId);

                var payload = new UnityAiBridgeGetHierarchyPayload
                {
                    filePath = metadata["filePath"] as string ?? outputPath,
                    fileSizeBytes = metadata.TryGetValue("fileSizeBytes", out var fileSizeValue)
                        ? Convert.ToInt64(fileSizeValue)
                        : 0L,
                    rootCount = nodeList.Count,
                    nodeCount = nodeCount,
                    activeScene = activeScenePayload,
                    filters = filters
                };

                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { getHierarchyResult = payload });
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }


        static UnityAiBridgeHierarchyFilters CreateHierarchyFilters(UnityAiBridgeProtocolRequestParameters parameters)
        {
            var useSelection = parameters.hierarchyUseSelection;
            var rootPath = string.IsNullOrWhiteSpace(parameters.hierarchyRootPath)
                ? string.Empty
                : parameters.hierarchyRootPath.Trim();
            var all = parameters.hierarchyAll || (!useSelection && string.IsNullOrEmpty(rootPath));
            var enabledFilterCount = (all ? 1 : 0) + (useSelection ? 1 : 0) + (string.IsNullOrEmpty(rootPath) ? 0 : 1);
            if (enabledFilterCount != 1)
            {
                throw new InvalidOperationException("Specify exactly one hierarchy filter: --all, --root-path, or --use-selection.");
            }

            return new UnityAiBridgeHierarchyFilters
            {
                all = all,
                rootPath = rootPath,
                useSelection = useSelection
            };
        }

        static List<Transform> CollectHierarchyRoots(Scene activeScene, UnityAiBridgeHierarchyFilters filters)
        {
            var sceneRoots = activeScene.GetRootGameObjects();
            if (filters.useSelection)
            {
                return CollectSelectedHierarchyRoots();
            }

            if (!string.IsNullOrEmpty(filters.rootPath))
            {
                foreach (var sceneRoot in sceneRoots)
                {
                    var match = FindHierarchyRootByPath(sceneRoot.transform, filters.rootPath);
                    if (match != null)
                    {
                        return new List<Transform> { match };
                    }
                }

                throw new InvalidOperationException($"Hierarchy root path was not found in the active scene: {filters.rootPath}");
            }

            var roots = new List<Transform>(sceneRoots.Length);
            foreach (var sceneRoot in sceneRoots)
            {
                if (sceneRoot != null)
                {
                    roots.Add(sceneRoot.transform);
                }
            }

            return roots;
        }

        static List<Transform> CollectSelectedHierarchyRoots()
        {
            var selectedTransforms = Selection.transforms;
            var results = new List<Transform>();
            if (selectedTransforms == null || selectedTransforms.Length == 0)
            {
                return results;
            }

            var selectedSet = new HashSet<Transform>(selectedTransforms);
            foreach (var selectedTransform in selectedTransforms)
            {
                if (selectedTransform == null)
                {
                    continue;
                }

                if (selectedTransform.parent != null && selectedSet.Contains(selectedTransform.parent))
                {
                    continue;
                }

                results.Add(selectedTransform);
            }

            return results;
        }

        static Transform FindHierarchyRootByPath(Transform current, string targetPath)
        {
            if (current == null)
            {
                return null;
            }

            if (string.Equals(BuildHierarchyPath(current.gameObject), targetPath, StringComparison.Ordinal))
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                var match = FindHierarchyRootByPath(current.GetChild(i), targetPath);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        static UnityAiBridgeHierarchyNode BuildHierarchyNode(Transform t, ref int nodeCount)
        {
            nodeCount++;
            var go = t.gameObject;
            var components = go.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] == null ? string.Empty : components[i].GetType().FullName ?? string.Empty;
            }

            var childNodes = new UnityAiBridgeHierarchyNode[t.childCount];
            for (int i = 0; i < t.childCount; i++)
            {
                childNodes[i] = BuildHierarchyNode(t.GetChild(i), ref nodeCount);
            }

            return new UnityAiBridgeHierarchyNode
            {
                name = go.name,
                path = BuildHierarchyPath(go),
                active = go.activeInHierarchy,
                tag = go.tag,
                layer = LayerMask.LayerToName(go.layer) ?? go.layer.ToString(),
                componentTypeNames = typeNames,
                children = childNodes
            };
        }
    }
}
