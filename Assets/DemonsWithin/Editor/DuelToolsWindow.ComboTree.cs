using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed partial class DuelToolsWindow
    {
        private const float ComboCardWidth = 174f;
        private const float ComboRow = 130f;
        private static readonly string[] BattleDomains = { "地面战斗", "空中战斗" };
        private static readonly Color GraphBackground = new Color32(25, 31, 40, 255);
        private static readonly Color GraphSelection = new Color32(249, 188, 94, 255);
        private static readonly Color GraphEdge = new Color32(99, 128, 153, 255);

        [SerializeField] private DuelActionAsset comboRoot;
        [SerializeField] private int battleDomain;
        private DuelActionAsset comboNode;
        private bool comboEdgeSelected;
        private float comboPreviewTime;
        private LucyCombatLibrary comboLibrary;
        private readonly List<TreeNode> comboNodes = new();
        private readonly List<TreeEdge> comboEdges = new();
        private readonly Dictionary<DuelActionAsset, TreeNode> comboLookup = new();
        private Vector2 comboPan;
        private Vector2 comboInspectorScroll;
        private float comboZoom = 1f;
        private bool comboPanning;
        private bool fitCombo = true;

        private sealed class TreeNode
        {
            public DuelActionAsset Action;
            public int Depth;
            public Rect Rect;
        }

        private readonly struct TreeEdge
        {
            public readonly TreeNode From, To;
            public readonly DuelCombo Asset;
            public readonly int Index;
            public ComboLink Link => Asset.Links[Index];

            public TreeEdge(TreeNode from, TreeNode to, DuelCombo asset, int index)
            {
                From = from;
                To = to;
                Asset = asset;
                Index = index;
            }
        }

        private void DrawComboWorkspace()
        {
            if (comboLibrary == null)
                comboLibrary = LucyCombatLibrary.Load();
            if (comboRoot == null)
                ChooseDefaultComboRoot();
            if (comboNode == null)
                comboNode = comboRoot;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24f)))
            {
                int next = GUILayout.Toolbar(battleDomain, BattleDomains, EditorStyles.toolbarButton, GUILayout.Width(208f));
                if (next != battleDomain)
                {
                    battleDomain = next;
                    ChooseDefaultComboRoot();
                }
                if (GUILayout.Button($"起手  {ShortActionName(comboRoot)} ▾", EditorStyles.toolbarDropDown,
                        GUILayout.MaxWidth(260f)))
                    ShowComboRoots();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("适合视图", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                    fitCombo = true;
            }

            const float top = 55f;
            float width = Mathf.Clamp(position.width * .35f, 314f, 430f);
            Rect canvas = new(6, top, position.width - width - 17f, position.height - top - 8f);
            Rect inspector = new(canvas.xMax + 8f, canvas.y, width, canvas.height);
            BuildComboTree();
            DrawComboTree(canvas);
            DrawComboInspector(inspector);
        }

        private void ChooseDefaultComboRoot()
        {
            DuelActionAsset root = battleDomain == 0 ? roleLoadout?.Find(DuelAction.Quick) : roleLoadout?.Air?.NeutralLaunch;
            if (root == null && comboLibrary != null)
            {
                string source = battleDomain == 0 ? "Lucy_FightFist02_1_Root" : "Lucy_Air_Attack_Combo01_1_Root";
                root = comboLibrary.Entries.Find(entry => entry.Source == source)?.Action;
            }
            SetComboRoot(root);
        }

        private void ShowComboRoots()
        {
            var menu = new GenericMenu();
            DuelActionAsset quick = roleLoadout?.Find(DuelAction.Quick);
            DuelActionAsset alternate = roleCombo?.AlternateOpener;
            var seen = new HashSet<DuelActionAsset> { quick, alternate };
            if (battleDomain == 0)
            {
                AddComboRoot(menu, "实战入口/左键 · ", quick);
                AddComboRoot(menu, "实战入口/C · ", alternate);
                AddComboRoot(menu, "实战入口/W+C · ", roleCombo?.ForwardOpener);
                AddComboRoot(menu, "实战入口/A或D+C · ", roleCombo?.SideOpener);
                AddComboRoot(menu, "实战入口/S+C · ", roleCombo?.BackOpener);
                seen.Add(roleCombo?.ForwardOpener);
                seen.Add(roleCombo?.SideOpener);
                seen.Add(roleCombo?.BackOpener);
            }
            else if (roleLoadout?.Air is DuelAirMoves air)
            {
                AddComboRoot(menu, "地面起跳/V · ", air.NeutralLaunch);
                AddComboRoot(menu, "地面起跳/W+V · ", air.ForwardLaunch);
                AddComboRoot(menu, "地面起跳/A或D+V · ", air.SideLaunch);
                AddComboRoot(menu, "地面起跳/S+V · ", air.BackLaunch);
            }

            AddAuthoredRoots(menu, roleCombo, seen);
            if (roleAdditionalCombos != roleCombo)
                AddAuthoredRoots(menu, roleAdditionalCombos, seen);
            if (comboLibrary != null)
            {
                LucyCombatKind kind = battleDomain == 0 ? LucyCombatKind.GroundAttack : LucyCombatKind.AirAttack;
                foreach (LucyCombatEntry entry in comboLibrary.Entries)
                    if (entry.Kind == kind && seen.Add(entry.Action))
                        AddComboRoot(menu, $"攻击片段/{entry.Take}/", entry.Action);
            }
            menu.ShowAsContext();
        }

        private void AddAuthoredRoots(GenericMenu menu, DuelCombo asset, HashSet<DuelActionAsset> seen)
        {
            if (asset == null)
                return;
            foreach (ComboLink link in asset.Links)
            {
                DuelActionAsset start = link?.From;
                if (start != null && IsAir(start) == (battleDomain == 1) && seen.Add(start))
                    AddComboRoot(menu, "已有派生/", start);
            }
        }

        private void AddComboRoot(GenericMenu menu, string group, DuelActionAsset root)
        {
            if (root != null)
                menu.AddItem(new GUIContent(group + ShortActionName(root), root.name), root == comboRoot,
                    () => SetComboRoot(root));
        }

        private bool IsAir(DuelActionAsset asset) =>
            asset != null && (asset.Space != ActionSpace.Ground ||
                comboLibrary?.Entries.Find(entry => entry.Action == asset)?.Kind == LucyCombatKind.AirAttack);

        private void SetComboRoot(DuelActionAsset root)
        {
            comboRoot = comboNode = root;
            comboEdgeSelected = false;
            comboInspectorScroll = Vector2.zero;
            fitCombo = true;
            ResetComboPlayback();
            Repaint();
        }

        private void BuildComboTree()
        {
            comboNodes.Clear();
            comboEdges.Clear();
            comboLookup.Clear();
            if (comboRoot == null)
                return;
            AddComboNode(comboRoot, 0);
            for (int i = 0; i < comboNodes.Count; i++)
            {
                TreeNode node = comboNodes[i];
                if (battleDomain == 0 && node.Depth > 0 && IsAir(node.Action))
                    continue;
                AddTreeChildren(node, roleCombo);
                if (roleAdditionalCombos != roleCombo)
                    AddTreeChildren(node, roleAdditionalCombos);
                if (combo != roleCombo && combo != roleAdditionalCombos)
                    AddTreeChildren(node, combo);
            }

            var counts = new int[comboNodes.Count];
            var offsets = new int[comboNodes.Count];
            foreach (TreeNode node in comboNodes)
                counts[node.Depth]++;
            foreach (TreeNode node in comboNodes)
            {
                int row = node.Depth;
                float x = (offsets[row]++ - (counts[row] - 1) * .5f) * (ComboCardWidth + 42f);
                node.Rect = new Rect(x - ComboCardWidth * .5f, node.Depth * ComboRow + 20f, ComboCardWidth, 58f);
            }
        }

        private TreeNode AddComboNode(DuelActionAsset asset, int depth)
        {
            if (!comboLookup.TryGetValue(asset, out TreeNode node))
            {
                node = new TreeNode { Action = asset, Depth = depth };
                comboLookup.Add(asset, node);
                comboNodes.Add(node);
            }
            return node;
        }

        private void AddTreeChildren(TreeNode node, DuelCombo asset)
        {
            if (asset == null)
                return;
            for (int i = 0; i < asset.Links.Count; i++)
            {
                ComboLink link = asset.Links[i];
                if (link?.From != node.Action || link.To == null)
                    continue;
                comboEdges.Add(new TreeEdge(node, AddComboNode(link.To, node.Depth + 1), asset, i));
            }
        }

        private void DrawComboTree(Rect viewport)
        {
            using (new GUI.GroupScope(viewport))
            {
                Rect area = new(Vector2.zero, viewport.size);
                if (fitCombo && comboNodes.Count > 0)
                    FitComboTree(area);
                HandleComboCanvas(area);
                HandleComboEdgeClick(area);
                EditorGUI.DrawRect(area, GraphBackground);
                DrawComboGrid(area);
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.BeginGUI();
                    foreach (TreeEdge edge in comboEdges)
                    {
                        Rect start = ComboView(edge.From.Rect);
                        Rect end = ComboView(edge.To.Rect);
                        Vector2 a = new(start.center.x, start.yMax);
                        Vector2 b = new(end.center.x, end.yMin);
                        float join = Mathf.Lerp(a.y, b.y, .46f);
                        Handles.color = EdgeSelected(edge) ? GraphSelection : GraphEdge;
                        Handles.DrawAAPolyLine(EdgeSelected(edge) ? 3f : 1.5f,
                            a, new Vector2(a.x, join), new Vector2(b.x, join), b);
                    }
                    Handles.color = Color.white;
                    Handles.EndGUI();
                }

                foreach (TreeNode node in comboNodes)
                    DrawComboCard(node);
                foreach (TreeEdge edge in comboEdges)
                    DrawComboEdgeLabel(edge);
                GUI.Label(new Rect(12, area.height - 23f, area.width - 24f, 21f),
                    "点击动作或派生线  ·  滚轮缩放  ·  中键平移", EditorStyles.miniLabel);
            }
        }

        private void FitComboTree(Rect area)
        {
            float half = ComboCardWidth * .5f;
            float bottom = 78f;
            foreach (TreeNode node in comboNodes)
            {
                half = Mathf.Max(half, Mathf.Abs(node.Rect.center.x) + node.Rect.width * .5f);
                bottom = Mathf.Max(bottom, node.Rect.yMax);
            }
            comboZoom = Mathf.Clamp(Mathf.Min((area.width - 26f) / (2f * half), (area.height - 48f) / bottom), .28f, 1.1f);
            comboPan = new Vector2(area.center.x, 16f);
            fitCombo = false;
        }

        private Rect ComboView(Rect rect) => new(rect.position * comboZoom + comboPan, rect.size * comboZoom);

        private void HandleComboCanvas(Rect area)
        {
            Event pointer = Event.current;
            if (pointer.type == EventType.MouseUp && comboPanning)
            {
                comboPanning = false;
                pointer.Use();
            }
            else if (pointer.type == EventType.MouseDrag && comboPanning)
            {
                comboPan += pointer.delta;
                pointer.Use();
                Repaint();
            }
            else if (area.Contains(pointer.mousePosition) && pointer.type == EventType.ScrollWheel)
            {
                Vector2 anchor = (pointer.mousePosition - comboPan) / comboZoom;
                comboZoom = Mathf.Clamp(comboZoom * Mathf.Pow(1.11f, -pointer.delta.y), .28f, 2.1f);
                comboPan = pointer.mousePosition - anchor * comboZoom;
                pointer.Use();
                Repaint();
            }
            else if (area.Contains(pointer.mousePosition) && pointer.type == EventType.MouseDown && pointer.button == 2)
            {
                comboPanning = true;
                pointer.Use();
            }
        }

        private void DrawComboGrid(Rect area)
        {
            float step = 28f * Mathf.Max(.55f, comboZoom);
            Color shade = new(1f, 1f, 1f, .043f);
            for (float x = Mathf.Repeat(comboPan.x, step); x < area.width; x += step)
                EditorGUI.DrawRect(new Rect(x, 0, 1f, area.height), shade);
            for (float y = Mathf.Repeat(comboPan.y, step); y < area.height; y += step)
                EditorGUI.DrawRect(new Rect(0, y, area.width, 1f), shade);
        }

        private void HandleComboEdgeClick(Rect area)
        {
            Event pointer = Event.current;
            Vector2 point = pointer.mousePosition;
            if (pointer.type != EventType.MouseDown || pointer.button != 0 || !area.Contains(point))
                return;
            foreach (TreeNode node in comboNodes)
                if (ComboView(node.Rect).Contains(point))
                    return;

            float nearest = 7f;
            TreeEdge? selected = null;
            foreach (TreeEdge edge in comboEdges)
            {
                Rect start = ComboView(edge.From.Rect);
                Rect end = ComboView(edge.To.Rect);
                Vector2 a = new(start.center.x, start.yMax);
                Vector2 b = new(end.center.x, end.yMin);
                float join = Mathf.Lerp(a.y, b.y, .46f);
                Vector2 first = new(a.x, join);
                Vector2 last = new(b.x, join);
                float distance = Mathf.Min(HandleUtility.DistancePointLine(point, a, first),
                    Mathf.Min(HandleUtility.DistancePointLine(point, first, last), HandleUtility.DistancePointLine(point, last, b)));
                if (distance < nearest)
                {
                    nearest = distance;
                    selected = edge;
                }
            }
            if (selected.HasValue)
            {
                SelectComboEdge(selected.Value.Asset, selected.Value.Index);
                pointer.Use();
            }
        }

        private void DrawComboCard(TreeNode node)
        {
            Rect rect = ComboView(node.Rect);
            bool selected = node.Action == comboNode;
            Color top = selected ? GraphSelection : IsAir(node.Action) ? new Color(.44f, .72f, .95f) : new Color(.40f, .77f, .68f);
            EditorGUI.DrawRect(rect, new Color32(46, 57, 72, 255));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, Mathf.Max(2f, 3f * comboZoom)), top);
            if (GUI.Button(rect, new GUIContent(ShortActionName(node.Action), node.Action.name), EditorStyles.centeredGreyMiniLabel))
            {
                comboNode = node.Action;
                comboEdgeSelected = false;
                comboInspectorScroll = Vector2.zero;
                ResetComboPlayback();
            }
            if ((node.Depth == 0 || battleDomain == 0 && IsAir(node.Action)) && comboZoom > .62f)
                GUI.Label(new Rect(rect.x, rect.yMax - 15f, rect.width, 14f),
                      ComboNodeRole(node.Action), EditorStyles.centeredGreyMiniLabel);
        }

        private string ComboNodeRole(DuelActionAsset action)
        {
            if (action.Space == ActionSpace.GroundLaunch) return "地面起跳";
            if (action.Space == ActionSpace.Air) return "空中续招";
            if (action == roleLoadout?.Find(DuelAction.Quick)) return "左键起手";
            if (action == roleCombo?.AlternateOpener) return "C 起手";
            if (action == roleCombo?.ForwardOpener) return "W+C 起手";
            if (action == roleCombo?.SideOpener) return "A/D+C 起手";
            return action == roleCombo?.BackOpener ? "S+C 起手" : "起手";
        }

        private void DrawComboEdgeLabel(TreeEdge edge)
        {
            var link = edge.Link;
            Rect end = ComboView(edge.To.Rect);
            float width = Mathf.Min(end.width + 8f, 126f);
            int incoming = 0;
            foreach (TreeEdge candidate in comboEdges)
            {
                if (candidate.Asset == edge.Asset && candidate.Index == edge.Index)
                    break;
                if (candidate.To == edge.To)
                    incoming++;
            }
            Rect label = new(end.center.x - width * .5f, end.yMin - (incoming + 1) * 19f - 2f, width, 19f);
            string input = DuelCombo.InputLabel(link.Input);
            string condition = ConditionName(link.RequiredResult);
            bool timingBranch = edge.Asset.Links.Exists(other => other != null && other != link && other.IsValid &&
                other.From == link.From && other.Input == link.Input && other.RequiredResult == link.RequiredResult);
            string range = $"{link.BufferFrom}–{link.InputCloseFrame - 1}f";
            string caption = timingBranch ? $"{input} · {range}" : $"{input} · {condition}";
            string tooltip = $"{input} · {condition} · 输入 {range}";
            if (GUI.Toggle(label, EdgeSelected(edge), new GUIContent(caption, tooltip), EditorStyles.miniButton) && !EdgeSelected(edge))
                SelectComboEdge(edge.Asset, edge.Index);
        }

        private bool EdgeSelected(TreeEdge edge) => comboEdgeSelected && combo == edge.Asset && linkIndex == edge.Index;

        private static string ShortActionName(DuelActionAsset asset) =>
            asset == null ? "未绑定" : asset.name.StartsWith("Lucy_") ? asset.name[5..] : asset.name;
    }
}
