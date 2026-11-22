using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed class DuelBehaviorWindow : EditorWindow
    {
        private const float NodeWidth = 170f;
        private const float NodeHeight = 83f;
        private const float Gap = 24f;
        private const float RootTop = 28f;
        private const float ChildTop = 211f;
        private const float ToolbarHeight = 27f;
        private static readonly Color Canvas = new Color32(25, 31, 40, 255);
        private static readonly Color Grid = new Color32(35, 43, 55, 255);
        private static readonly Color Panel = new Color32(45, 54, 67, 255);
        private static readonly Color Muted = new Color32(143, 158, 176, 255);
        private static readonly Color Bright = new Color32(226, 234, 243, 255);
        private static readonly Color Selected = new Color32(249, 188, 94, 255);
        private static readonly Color Running = new Color32(87, 218, 181, 255);

        [SerializeField] private DuelBehaviorTree tree;
        [SerializeField] private DuelOpponent target;
        [SerializeField] private int selectedNode = -1;
        private Vector2 inspectorScroll;
        private Vector2 pan;
        private float zoom = 1f;
        private bool fitPending = true;
        private bool panning;
        private GUIStyle nodeTitle;
        private GUIStyle nodeCaption;
        private GUIStyle controlsHint;

        [MenuItem("Tools/Demons Within/心魔行为树", priority = 103)]
        public static void Open() => GetWindow<DuelBehaviorWindow>("心魔 · 行为树").Show();

        private void OnEnable()
        {
            minSize = new Vector2(740f, 420f);
            if (tree == null)
                tree = Resources.Load<DuelBehaviorTree>("DemonBehaviorTree");
            fitPending = true;
        }

        private void OnInspectorUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;
            if (target == null)
                target = FindAnyObjectByType<DuelOpponent>();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (tree == null)
                return;

            var settings = new SerializedObject(tree);
            SerializedProperty nodes = settings.FindProperty("Nodes");
            selectedNode = Mathf.Clamp(selectedNode, -1, nodes.arraySize - 1);
            bool live = EditorApplication.isPlaying && target != null && target.BehaviorTree == tree;
            float panelWidth = Mathf.Clamp(position.width * 0.30f, 245f, 320f);
            float graphWidth = position.width - panelWidth;
            Rect graph = new(0, ToolbarHeight, graphWidth, position.height - ToolbarHeight);
            Rect inspector = new(graphWidth, ToolbarHeight, panelWidth, graph.height);

            if (fitPending && graph.width > 1f && graph.height > 1f)
                Fit(graph.size, nodes.arraySize);
            DrawGraph(graph, nodes, live);
            DrawInspector(inspector, settings, nodes, live);
            settings.ApplyModifiedProperties();
        }

        private void DrawToolbar()
        {
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
            {
                GUILayout.Label("行为树", GUILayout.Width(44f));
                var chosen = (DuelBehaviorTree)EditorGUILayout.ObjectField(tree, typeof(DuelBehaviorTree), false,
                    GUILayout.MaxWidth(235f), GUILayout.MinWidth(95f));
                if (chosen != tree)
                {
                    selectedNode = -1;
                    tree = chosen;
                    fitPending = true;
                }

                if (EditorApplication.isPlaying)
                    target = (DuelOpponent)EditorGUILayout.ObjectField(target, typeof(DuelOpponent), true,
                        GUILayout.MaxWidth(175f), GUILayout.MinWidth(80f));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{zoom:P0}", EditorStyles.miniLabel, GUILayout.Width(43f));
                if (GUILayout.Button("适合视图", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    fitPending = true;
            }
        }

        private void Fit(Vector2 view, int count)
        {
            float graphWidth = Mathf.Max(NodeWidth, count * (NodeWidth + Gap) - Gap);
            zoom = Mathf.Clamp(Mathf.Min((view.x - 44f) / graphWidth, (view.y - 48f) / (ChildTop + NodeHeight)),
                0.30f, 1.2f);
            pan = new Vector2(view.x * 0.5f, Mathf.Max(4f, (view.y - (ChildTop + NodeHeight) * zoom) / 2f));
            fitPending = false;
        }

        private static Rect NodeRect(int index, int count)
        {
            float x = index < 0 ? -NodeWidth * 0.5f :
                (index - (count - 1) * 0.5f) * (NodeWidth + Gap) - NodeWidth * 0.5f;
            return new Rect(x, index < 0 ? RootTop : ChildTop, NodeWidth, NodeHeight);
        }

        private Rect ToView(Rect node) => new(node.position * zoom + pan, node.size * zoom);

        private void DrawGraph(Rect viewport, SerializedProperty nodes, bool live)
        {
            GUI.BeginGroup(viewport);
            Rect bounds = new(Vector2.zero, viewport.size);
            HandleGraphInput(bounds, nodes.arraySize);
            EditorGUI.DrawRect(bounds, Canvas);
            DrawGrid(bounds);

            Rect root = ToView(NodeRect(-1, nodes.arraySize));
            float junction = Mathf.Lerp(root.yMax, ChildTop * zoom + pan.y, 0.48f);
            for (int i = 0; i < nodes.arraySize; i++)
                DrawEdge(root, ToView(NodeRect(i, nodes.arraySize)), junction, new Color32(79, 96, 116, 255), 1.5f);

            if (selectedNode >= 0)
                DrawEdge(root, ToView(NodeRect(selectedNode, nodes.arraySize)), junction, Selected, 2f);
            if (live && target.ActiveNode >= 0 && target.ActiveNode < nodes.arraySize)
                DrawEdge(root, ToView(NodeRect(target.ActiveNode, nodes.arraySize)), junction, Running, 2.5f);

            DrawCard(root, "优先选择器", "根节点", -1, live && target.ActiveNode >= 0 ? Running : Muted);
            for (int i = 0; i < nodes.arraySize; i++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(i);
                DuelBehavior type = NodeType(node);
                bool enabled = node.FindPropertyRelative("Enabled").boolValue;
                DuelNodeState state = live ? target.StateAt(i) : DuelNodeState.Skipped;
                string status = !enabled ? "已禁用" : live ? StateName(state) : "已启用";
                Color accent = !enabled ? Muted : state is DuelNodeState.Running or DuelNodeState.Success ? Running :
                    state == DuelNodeState.Failure ? new Color32(215, 144, 102, 255) : new Color32(128, 170, 220, 255);
                DrawCard(ToView(NodeRect(i, nodes.arraySize)), NodeName(type), $"{i + 1:00}  {status}", i, accent);
            }

            if (Event.current.type == EventType.Repaint)
            {
                controlsHint ??= new GUIStyle(EditorStyles.miniLabel);
                controlsHint.normal.textColor = Muted;
                GUI.Label(new Rect(14, bounds.yMax - 27, bounds.width - 24, 20),
                    "点击节点  ·  滚轮缩放  ·  中键拖动画布", controlsHint);
            }
            GUI.EndGroup();
        }

        private void HandleGraphInput(Rect bounds, int count)
        {
            Event input = Event.current;
            if (input.type == EventType.MouseUp && panning)
            {
                panning = false;
                input.Use();
                return;
            }

            if (input.type == EventType.MouseDrag && panning)
            {
                pan += input.delta;
                input.Use();
                Repaint();
                return;
            }

            if (!bounds.Contains(input.mousePosition))
                return;
            if (input.type == EventType.ScrollWheel)
            {
                Vector2 anchor = (input.mousePosition - pan) / zoom;
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.11f, -input.delta.y), 0.3f, 2.25f);
                pan = input.mousePosition - anchor * zoom;
                input.Use();
                Repaint();
                return;
            }
            if (input.type != EventType.MouseDown)
                return;

            if (input.button == 2 || (input.button == 0 && input.alt))
            {
                panning = true;
                input.Use();
                return;
            }
            if (input.button != 0)
                return;

            for (int index = -1; index < count; index++)
            {
                if (!ToView(NodeRect(index, count)).Contains(input.mousePosition))
                    continue;

                selectedNode = index;
                inspectorScroll = Vector2.zero;
                GUI.FocusControl(null);
                input.Use();
                Repaint();
                return;
            }
        }

        private void DrawGrid(Rect bounds)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            float spacing = 32f * zoom;
            for (float x = Mathf.Repeat(pan.x, spacing); x < bounds.width; x += spacing)
                EditorGUI.DrawRect(new Rect(x, 0, 1f, bounds.height), Grid);
            for (float y = Mathf.Repeat(pan.y, spacing); y < bounds.height; y += spacing)
                EditorGUI.DrawRect(new Rect(0, y, bounds.width, 1f), Grid);
        }

        private static void DrawEdge(Rect from, Rect to, float junction, Color color, float width)
        {
            float start = from.center.x;
            float end = to.center.x;
            EditorGUI.DrawRect(new Rect(start - width * 0.5f, from.yMax, width, junction - from.yMax), color);
            EditorGUI.DrawRect(new Rect(Mathf.Min(start, end), junction, Mathf.Abs(end - start) + width, width), color);
            EditorGUI.DrawRect(new Rect(end - width * 0.5f, junction, width, to.yMin - junction), color);
        }

        private void DrawCard(Rect rect, string title, string caption, int index, Color accent)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            bool selected = selectedNode == index;
            if (selected)
                EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), Selected);
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 4, rect.width, rect.height), new Color(0, 0, 0, 0.29f));
            EditorGUI.DrawRect(rect, index < 0 ? new Color32(41, 61, 79, 255) : Panel);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, Mathf.Max(2f, 4f * zoom)), accent);
            float socket = Mathf.Max(8f, 17f * zoom);
            float socketY = index < 0 ? rect.yMax : rect.yMin - 4f;
            EditorGUI.DrawRect(new Rect(rect.center.x - socket * 0.5f, socketY, socket, 4f), accent);

            nodeTitle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            nodeCaption ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            nodeTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(15f * zoom), 9, 27);
            nodeTitle.normal.textColor = Bright;
            nodeCaption.fontSize = Mathf.Clamp(Mathf.RoundToInt(11f * zoom), 8, 20);
            nodeCaption.normal.textColor = accent;
            GUI.Label(new Rect(rect.x + 2, rect.y + rect.height * 0.18f, rect.width - 4, rect.height * 0.44f), title, nodeTitle);
            if (rect.height > 34f)
                GUI.Label(new Rect(rect.x + 2, rect.y + rect.height * 0.63f, rect.width - 4, rect.height * 0.25f), caption, nodeCaption);
        }

        private void DrawInspector(Rect area, SerializedObject settings, SerializedProperty nodes, bool live)
        {
            EditorGUI.DrawRect(new Rect(area.x, area.y, 1f, area.height), new Color32(94, 109, 129, 255));
            GUILayout.BeginArea(new Rect(area.x + 10, area.y + 11, area.width - 20, area.height - 18));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUILayout.LabelField("节点参数", EditorStyles.largeLabel);
            GUILayout.Space(7f);

            if (selectedNode < 0)
            {
                EditorGUILayout.LabelField("优先选择器", EditorStyles.boldLabel);
                GUILayout.Label("从左到右检查子节点，遇到运行中或成功的节点时结束本次检查。", EditorStyles.wordWrappedMiniLabel);
                RootField(settings.FindProperty("AttackRangeMargin"), "出手距离余量（米）");
                RootField(settings.FindProperty("BaselineProbeInterval"), "基线探测间隔");
            }
            else
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(selectedNode);
                DuelBehavior type = NodeType(node);
                EditorGUILayout.LabelField(NodeName(type), EditorStyles.boldLabel);
                GUILayout.Label(Description(type), EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(6f);
                EditorGUILayout.PropertyField(node.FindPropertyRelative("Enabled"), new GUIContent("启用节点"));

                switch (type)
                {
                    case DuelBehavior.Approach:
                        RootField(node.FindPropertyRelative("MoveInput"), "接近移动强度");
                        break;
                    case DuelBehavior.RecoverSpacing:
                        RootField(node.FindPropertyRelative("MoveInput"), "后退移动强度");
                        RootField(node.FindPropertyRelative("NearDistance"), "过近距离（米）");
                        break;
                    case DuelBehavior.Strike:
                        RootField(node.FindPropertyRelative("Cooldown"), "出手间隔（秒）");
                        break;
                }

                GUILayout.Space(8f);
                EditorGUILayout.LabelField($"执行优先级  {selectedNode + 1} / {nodes.arraySize}", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Move(nodes, -1, "← 提前") || Move(nodes, 1, "推后 →"))
                        Repaint();
                }
            }

            GUILayout.Space(10f);
            if (live)
            {
                string mode = target.IsLlmMode ? "LLM 战术" : target.IsAdaptive ? "本局自适应" : "固定选招";
                EditorGUILayout.LabelField(mode, EditorStyles.boldLabel);
                if (target.IsLlmMode)
                    EditorGUILayout.LabelField(target.Tactics.Status, EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"冷却 {target.CooldownRemaining:0.00} s");
                if (target.LastChoice.Chosen != DuelAction.None)
                    EditorGUILayout.LabelField($"最近出招  {target.LastChoice.Chosen}", target.LastChoice.PlanTactic);
                if (selectedNode >= 0)
                    EditorGUILayout.LabelField($"本次状态  {StateName(target.StateAt(selectedNode))}");
            }
            else if (EditorApplication.isPlaying && target != null)
            {
                EditorGUILayout.HelpBox("所选观察实例使用另一份行为树资产。", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void RootField(SerializedProperty property, string label)
        {
            GUILayout.Space(5f);
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(property, GUIContent.none);
        }

        private bool Move(SerializedProperty nodes, int change, string label)
        {
            int nextIndex = selectedNode + change;
            using (new EditorGUI.DisabledScope(nextIndex < 0 || nextIndex >= nodes.arraySize))
            {
                if (!GUILayout.Button(label, GUILayout.MinHeight(24)))
                    return false;
                nodes.MoveArrayElement(selectedNode, nextIndex);
                selectedNode = nextIndex;
                return true;
            }
        }

        private static DuelBehavior NodeType(SerializedProperty node) =>
            (DuelBehavior)node.FindPropertyRelative("Type").intValue;

        private static string NodeName(DuelBehavior node) => node switch
        {
            DuelBehavior.FinishCommitted => "完成当前动作",
            DuelBehavior.Approach => "接近对手",
            DuelBehavior.RecoverSpacing => "调整站位",
            _ => "选招攻击"
        };

        private static string Description(DuelBehavior node) => node switch
        {
            DuelBehavior.FinishCommitted => "攻击、受击或其他动作正在执行时等待释放。",
            DuelBehavior.Approach => "当前候选招式够不到对手时向前移动。",
            DuelBehavior.RecoverSpacing => "出招冷却期间保持距离，过近则后退。",
            _ => "冷却结束且距离合适时出招。本局自适应会根据已观察到的玩家行为调整选招。"
        };

        private static string StateName(DuelNodeState state) => state switch
        {
            DuelNodeState.Failure => "未满足",
            DuelNodeState.Running => "运行中",
            DuelNodeState.Success => "已执行",
            _ => "已跳过"
        };
    }
}
