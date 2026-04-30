using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityGit.Editor
{
    public sealed class UnityGitHistoryGraphWindow : EditorWindow
    {
        private const string WindowTitle = "Lux Git History Graph";
        private GitHistorySnapshot _historySnapshot;
        private Vector2 _scrollPosition;
        private int _refreshGeneration;

        [MenuItem("Window/Linalab/Lux/Git History Graph")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityGitHistoryGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(600f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            RefreshAll();
        }

        private static string GetProjectRoot()
        {
            if (string.IsNullOrEmpty(Application.dataPath))
            {
                return string.Empty;
            }

            var assetsDirectory = new System.IO.DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? string.Empty : assetsDirectory.Parent.FullName;
        }

        private void RefreshAll()
        {
            var generation = ++_refreshGeneration;
            var projectRoot = GetProjectRoot();

            System.Threading.Tasks.Task.Run(() =>
            {
                var history = UnityGitHistoryService.ReadHistory(projectRoot, 100);

                EditorApplication.delayCall += () =>
                {
                    if (generation != _refreshGeneration) return;
                    _historySnapshot = history;
                    Repaint();
                };
            });
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("History Graph", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    RefreshAll();
                }
            }

            if (_historySnapshot == null)
            {
                EditorGUILayout.HelpBox("Loading history...", MessageType.Info);
                return;
            }

            if (!_historySnapshot.IsRepository)
            {
                EditorGUILayout.HelpBox(_historySnapshot.ErrorMessage, MessageType.Warning);
                return;
            }

            if (_historySnapshot.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox(_historySnapshot.FriendlyMessage, MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawGraph();

            EditorGUILayout.EndScrollView();
        }

        private void DrawGraph()
        {
            var entries = _historySnapshot.Entries;
            if (entries.Count == 0) return;

            float rowHeight = 24f;
            float nodeRadius = 5f;
            float laneWidth = 16f;
            float startX = 20f;
            float startY = 10f;

            var activeBranches = new List<string>();
            var commitLanes = new int[entries.Count];

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                
                int lane = -1;
                for (int j = 0; j < activeBranches.Count; j++)
                {
                    if (activeBranches[j] == entry.Hash)
                    {
                        lane = j;
                        break;
                    }
                }

                if (lane == -1)
                {
                    for (int j = 0; j < activeBranches.Count; j++)
                    {
                        if (activeBranches[j] == null)
                        {
                            lane = j;
                            break;
                        }
                    }
                    if (lane == -1)
                    {
                        lane = activeBranches.Count;
                        activeBranches.Add(null);
                    }
                }

                commitLanes[i] = lane;
                activeBranches[lane] = null;

                foreach (var parent in entry.ParentHashes)
                {
                    bool found = false;
                    for (int j = 0; j < activeBranches.Count; j++)
                    {
                        if (activeBranches[j] == parent)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        if (activeBranches[lane] == null)
                        {
                            activeBranches[lane] = parent;
                        }
                        else
                        {
                            int emptyLane = -1;
                            for (int j = 0; j < activeBranches.Count; j++)
                            {
                                if (activeBranches[j] == null)
                                {
                                    emptyLane = j;
                                    break;
                                }
                            }
                            if (emptyLane == -1)
                            {
                                activeBranches.Add(parent);
                            }
                            else
                            {
                                activeBranches[emptyLane] = parent;
                            }
                        }
                    }
                }
            }

            int maxLane = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (commitLanes[i] > maxLane) maxLane = commitLanes[i];
            }

            float textStartX = startX + (maxLane + 1) * laneWidth + 10f;

            if (Event.current.type == EventType.Repaint)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    int lane = commitLanes[i];
                    Vector2 nodePos = new Vector2(startX + lane * laneWidth, startY + i * rowHeight + rowHeight / 2f);

                    foreach (var parent in entry.ParentHashes)
                    {
                        int parentIndex = -1;
                        for (int j = i + 1; j < entries.Count; j++)
                        {
                            if (entries[j].Hash == parent)
                            {
                                parentIndex = j;
                                break;
                            }
                        }

                        if (parentIndex != -1)
                        {
                            int parentLane = commitLanes[parentIndex];
                            Vector2 parentPos = new Vector2(startX + parentLane * laneWidth, startY + parentIndex * rowHeight + rowHeight / 2f);
                            
                            Handles.color = GetLaneColor(lane);
                            Handles.DrawAAPolyLine(3f, nodePos, parentPos);
                        }
                        else
                        {
                            Handles.color = GetLaneColor(lane);
                            Handles.DrawAAPolyLine(3f, nodePos, nodePos + new Vector2(0, rowHeight));
                        }
                    }
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                int lane = commitLanes[i];
                
                Rect rowRect = new Rect(0, startY + i * rowHeight, position.width, rowHeight);
                
                if (Event.current.type == EventType.Repaint)
                {
                    if (i % 2 == 1)
                    {
                        EditorGUI.DrawRect(rowRect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.05f));
                    }

                    Vector2 nodePos = new Vector2(startX + lane * laneWidth, startY + i * rowHeight + rowHeight / 2f);
                    Handles.color = GetLaneColor(lane);
                    Handles.DrawSolidDisc(nodePos, Vector3.forward, nodeRadius);
                    Handles.color = Color.black;
                    Handles.DrawWireDisc(nodePos, Vector3.forward, nodeRadius);
                }

                Rect textRect = new Rect(textStartX, startY + i * rowHeight, position.width - textStartX, rowHeight);
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(textStartX);
                    EditorGUILayout.LabelField(entry.ShortHash, EditorStyles.boldLabel, GUILayout.Width(60f));
                    EditorGUILayout.LabelField(entry.Subject, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField(entry.AuthorName, EditorStyles.miniLabel, GUILayout.Width(100f));
                    EditorGUILayout.LabelField(entry.AuthoredAt.ToString("yyyy-MM-dd HH:mm"), EditorStyles.miniLabel, GUILayout.Width(120f));
                }
            }
        }

        private Color GetLaneColor(int lane)
        {
            Color[] colors = new Color[]
            {
                new Color(0.3f, 0.6f, 1.0f),
                new Color(0.3f, 0.8f, 0.3f),
                new Color(0.9f, 0.3f, 0.3f),
                new Color(0.9f, 0.9f, 0.3f),
                new Color(0.8f, 0.3f, 0.8f),
                new Color(0.3f, 0.8f, 0.8f),
                new Color(0.9f, 0.6f, 0.3f)
            };
            return colors[lane % colors.Length];
        }
    }
}
