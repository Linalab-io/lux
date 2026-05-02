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
        static UnityAiBridgeProtocolResponse FindGameObjects(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var searchMode = string.IsNullOrWhiteSpace(parameters.findGameObjectsSearchMode)
                ? "query"
                : parameters.findGameObjectsSearchMode.Trim().ToLowerInvariant();
            var inlineLimit = parameters.findGameObjectsInlineLimit <= 0 ? 50 : parameters.findGameObjectsInlineLimit;
            var activeState = string.IsNullOrWhiteSpace(parameters.findGameObjectsActiveState)
                ? "any"
                : parameters.findGameObjectsActiveState.Trim().ToLowerInvariant();

            try
            {
                var roots = CollectRootGameObjects(searchMode);
                var matches = new List<GameObject>();
                Regex nameRegex = null;
                if (!string.IsNullOrEmpty(parameters.findGameObjectsRegex))
                {
                    nameRegex = new Regex(parameters.findGameObjectsRegex);
                }

                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    if (searchMode == "selected")
                    {
                        if (MatchesFilters(root, parameters, nameRegex, activeState))
                        {
                            matches.Add(root);
                        }
                        continue;
                    }

                    CollectMatches(root.transform, parameters, nameRegex, activeState, matches);
                }

                var totalMatchCount = matches.Count;
                var returnedCount = Math.Min(totalMatchCount, inlineLimit);
                var truncated = totalMatchCount > inlineLimit;
                var entries = new UnityAiBridgeFindGameObjectsEntry[returnedCount];
                for (int i = 0; i < returnedCount; i++)
                {
                    entries[i] = BuildEntry(matches[i]);
                }

                string outputFilePath = string.Empty;
                long fileSizeBytes = 0L;
                if (truncated)
                {
                    var allEntries = new UnityAiBridgeFindGameObjectsEntry[totalMatchCount];
                    for (int i = 0; i < totalMatchCount; i++)
                    {
                        allEntries[i] = BuildEntry(matches[i]);
                    }
                    outputFilePath = WriteFindGameObjectsOutput(request.requestId, allEntries, out fileSizeBytes);
                }

                var payload = new UnityAiBridgeFindGameObjectsPayload
                {
                    searchMode = searchMode,
                    totalMatchCount = totalMatchCount,
                    returnedCount = returnedCount,
                    truncated = truncated,
                    outputFilePath = outputFilePath,
                    fileSizeBytes = fileSizeBytes,
                    gameObjects = entries
                };

                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { findGameObjectsResult = payload });
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }

        static List<GameObject> CollectRootGameObjects(string searchMode)
        {
            var roots = new List<GameObject>();
            if (searchMode == "selected")
            {
                var selected = Selection.gameObjects;
                if (selected != null)
                {
                    roots.AddRange(selected);
                }
                return roots;
            }

            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return roots;
            }

            roots.AddRange(activeScene.GetRootGameObjects());
            return roots;
        }

        static void CollectMatches(Transform current, UnityAiBridgeProtocolRequestParameters parameters, Regex nameRegex, string activeState, List<GameObject> matches)
        {
            if (current == null)
            {
                return;
            }

            if (MatchesFilters(current.gameObject, parameters, nameRegex, activeState))
            {
                matches.Add(current.gameObject);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                CollectMatches(current.GetChild(i), parameters, nameRegex, activeState, matches);
            }
        }

        static bool MatchesFilters(GameObject candidate, UnityAiBridgeProtocolRequestParameters parameters, Regex nameRegex, string activeState)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsName) &&
                !candidate.name.Equals(parameters.findGameObjectsName, StringComparison.Ordinal))
            {
                return false;
            }

            if (nameRegex != null && !nameRegex.IsMatch(candidate.name))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsPath))
            {
                var path = BuildHierarchyPath(candidate);
                if (path.IndexOf(parameters.findGameObjectsPath, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsTag) &&
                !candidate.CompareTag(parameters.findGameObjectsTag))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsLayer))
            {
                var layerName = LayerMask.LayerToName(candidate.layer) ?? string.Empty;
                if (!layerName.Equals(parameters.findGameObjectsLayer, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsComponent))
            {
                bool hasComponent = false;
                var components = candidate.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }
                    var typeName = component.GetType().Name;
                    var fullTypeName = component.GetType().FullName ?? string.Empty;
                    if (typeName.Equals(parameters.findGameObjectsComponent, StringComparison.Ordinal) ||
                        fullTypeName.Equals(parameters.findGameObjectsComponent, StringComparison.Ordinal))
                    {
                        hasComponent = true;
                        break;
                    }
                }
                if (!hasComponent)
                {
                    return false;
                }
            }

            if (activeState == "active" && !candidate.activeInHierarchy)
            {
                return false;
            }
            if (activeState == "inactive" && candidate.activeInHierarchy)
            {
                return false;
            }

            return true;
        }

        static UnityAiBridgeFindGameObjectsEntry BuildEntry(GameObject gameObject)
        {
            var components = gameObject.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] == null ? string.Empty : components[i].GetType().FullName ?? string.Empty;
            }

            return new UnityAiBridgeFindGameObjectsEntry
            {
                name = gameObject.name,
                hierarchyPath = BuildHierarchyPath(gameObject),
                sceneName = gameObject.scene.name ?? string.Empty,
                scenePath = gameObject.scene.path ?? string.Empty,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                tag = gameObject.tag,
                layer = gameObject.layer,
                layerName = LayerMask.LayerToName(gameObject.layer) ?? string.Empty,
                instanceId = gameObject.GetInstanceID(),
                componentTypeNames = typeNames
            };
        }

        static string BuildHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }
            var stack = new Stack<string>();
            var current = gameObject.transform;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", stack.ToArray());
        }

        static string WriteFindGameObjectsOutput(string requestId, UnityAiBridgeFindGameObjectsEntry[] entries, out long fileSizeBytes)
        {
            var projectRoot = GetProjectRoot();
            var outputDirectory = Path.Combine(projectRoot, ".lux", "outputs", "find-game-objects");
            Directory.CreateDirectory(outputDirectory);
            var safeRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            var outputPath = Path.Combine(outputDirectory, safeRequestId + ".json");
            var json = JsonUtility.ToJson(new UnityAiBridgeFindGameObjectsPayload
            {
                searchMode = string.Empty,
                totalMatchCount = entries.Length,
                returnedCount = entries.Length,
                truncated = false,
                outputFilePath = string.Empty,
                fileSizeBytes = 0L,
                gameObjects = entries
            }, true);
            File.WriteAllText(outputPath, json);
            fileSizeBytes = new FileInfo(outputPath).Length;
            return outputPath;
        }
    }
}
