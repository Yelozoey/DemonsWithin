using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed partial class DuelToolsWindow
    {
        private ComboLink SelectedComboEdge => comboEdgeSelected && combo != null &&
            linkIndex >= 0 && linkIndex < combo.Links.Count ? combo.Links[linkIndex] : null;

        private float ComboPreviewDuration
        {
            get
            {
                ComboLink edge = SelectedComboEdge;
                if (edge?.From != null && edge.To != null)
                    return ComboLead(edge) + (float)edge.To.Length / edge.To.Rate;
                return comboNode != null ? (float)comboNode.Length / comboNode.Rate : 0f;
            }
        }

        private static float ComboLead(ComboLink edge) => Mathf.Min(.28f, (float)edge.OpenFrame / edge.From.Rate);

        private void DrawComboInspector(Rect area)
        {
            float fieldsHeight = Mathf.Min(Mathf.Max(182f, area.height * .59f), area.height - 148f);
            Rect fields = new(area.x, area.y, area.width, fieldsHeight);
            Rect viewport = new(area.x, fields.yMax + 7f, area.width, Mathf.Max(1f, area.yMax - fields.yMax - 7f));
            using (new GUILayout.AreaScope(fields))
            {
                EditorGUILayout.LabelField(BattleDomains[battleDomain], EditorStyles.boldLabel);
                using (var scroller = new GUILayout.ScrollViewScope(comboInspectorScroll))
                {
                    comboInspectorScroll = scroller.scrollPosition;
                    if (SelectedComboEdge != null)
                        DrawComboEdgeInspector();
                    else if (comboNode != null)
                        DrawComboNodeInspector();
                    if (ComboPreviewDuration > 0f)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            PlaybackButton(ref comboPreviewTime, ComboPreviewDuration);
                            EditorGUI.BeginChangeCheck();
                            float time = GUILayout.HorizontalSlider(comboPreviewTime, 0f, ComboPreviewDuration);
                            if (EditorGUI.EndChangeCheck())
                            {
                                comboPreviewTime = time;
                                playing = false;
                            }
                        }
                    }
                }
            }
            DrawComboAnimation(viewport);
        }

        private void DrawComboNodeInspector()
        {
            EditorGUILayout.LabelField(new GUIContent(comboNode.name, comboNode.name), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("片段", $"{comboNode.Length} 帧 / {comboNode.Rate} fps");
            if (comboNode.Attack)
                EditorGUILayout.LabelField("命中窗口", $"{comboNode.HitStart}–{comboNode.HitEnd} 帧");
            if (battleDomain == 0 && IsAir(comboNode) && GUILayout.Button("查看空中派生"))
            {
                battleDomain = 1;
                SetComboRoot(comboNode);
            }
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("添加派生"))
                    AddComboBranch(comboNode);
                if (GUILayout.Button("打开动作时间轴"))
                {
                    SetAction(comboNode);
                    ChangeTab(2);
                }
            }

            if (!IsAir(comboNode) && comboNode.Attack && roleCombo != null && GUILayout.Button("设为地面起手…"))
                ShowGroundStarterSlots();
        }

        private void ShowGroundStarterSlots()
        {
            var menu = new GenericMenu();
            DuelCombo asset = roleCombo;
            DuelActionAsset action = comboNode;
            AddSlot("C", nameof(DuelCombo.AlternateOpener), asset.AlternateOpener);
            AddSlot("W+C", nameof(DuelCombo.ForwardOpener), asset.ForwardOpener);
            AddSlot("A或D+C", nameof(DuelCombo.SideOpener), asset.SideOpener);
            AddSlot("S+C", nameof(DuelCombo.BackOpener), asset.BackOpener);
            menu.ShowAsContext();

            void AddSlot(string input, string property, DuelActionAsset current) =>
                menu.AddItem(new GUIContent(input), action == current, () =>
                {
                    var serialized = new SerializedObject(asset);
                    serialized.FindProperty(property).objectReferenceValue = action;
                    if (serialized.ApplyModifiedProperties())
                        AssetDatabase.SaveAssetIfDirty(asset);
                    Repaint();
                });
        }

        private void DrawComboEdgeInspector()
        {
            ComboLink link = SelectedComboEdge;
            var serialized = new SerializedObject(combo);
            SerializedProperty edge = serialized.FindProperty("Links").GetArrayElementAtIndex(linkIndex);
            EditorGUILayout.LabelField($"{ShortActionName(link.From)} → {ShortActionName(link.To)}", EditorStyles.boldLabel);
            Field(edge, "From", "前一动作");
            Field(edge, "Input", "派生输入");
            Field(edge, "To", "后一动作");
            DrawComboCondition(edge, link.RequiredResult);
            Field(edge, "BufferFrom", "开始缓存帧");
            Field(edge, "BufferUntil", "缓存截止帧（0 跟随派生）");
            Field(edge, "CancelFrom", "最早派生帧");
            Field(edge, "CancelUntil", "截止帧（不含）");

            if (link.From != null && link.To != null && !link.IsValid)
                EditorGUILayout.HelpBox("检查缓存区间、命中结束后的派生区间，以及后一动作是否为攻击。", MessageType.Warning);
            else if (link.IsValid && combo.Links.Exists(other => other != null && other != link && other.IsValid &&
                other.From == link.From && other.Input == link.Input && other.RequiredResult == link.RequiredResult &&
                Mathf.Max(other.BufferFrom, link.BufferFrom) < Mathf.Min(other.InputCloseFrame, link.InputCloseFrame)))
                EditorGUILayout.HelpBox("这一派生与同键、同结果的另一条线共享缓存帧。可调整缓存起止帧来区分早按和晚按。", MessageType.Warning);

            if (GUILayout.Button("删除派生线"))
            {
                serialized.FindProperty("Links").DeleteArrayElementAtIndex(linkIndex);
                serialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(combo);
                comboEdgeSelected = false;
                comboNode = link.From;
                fitCombo = true;
                ResetComboPlayback();
                Repaint();
                return;
            }
            if (serialized.ApplyModifiedProperties())
            {
                AssetDatabase.SaveAssetIfDirty(combo);
                ResetComboPlayback();
                Repaint();
            }
        }

        private void AddComboBranch(DuelActionAsset from)
        {
            DuelCombo asset = (AssetDatabase.IsSubAsset(from) ? roleAdditionalCombos : roleCombo) ??
                roleCombo ?? combo;
            if (asset == null)
                return;

            Undo.RecordObject(asset, "添加连招派生");
            asset.Links.Add(new ComboLink
            {
                From = from, Input = DuelAction.Quick, BufferFrom = Mathf.Max(0, from.HitStart - 2),
                CancelFrom = from.HitEnd, CancelUntil = from.Length + 1
            });
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            SelectComboEdge(asset, asset.Links.Count - 1);
        }

        private void SelectComboEdge(DuelCombo asset, int index)
        {
            combo = asset;
            linkIndex = index;
            ComboLink link = asset.Links[index];
            comboNode = link.To != null ? link.To : link.From;
            comboEdgeSelected = true;
            comboInspectorScroll = Vector2.zero;
            ResetComboPlayback();
            Repaint();
        }

        private void FocusComboBranch(DuelCombo asset, int index)
        {
            comboLibrary ??= LucyCombatLibrary.Load();
            ComboLink link = asset.Links[index];
            battleDomain = IsAir(link.From) ? 1 : 0;
            SetComboRoot(link.From);
            SelectComboEdge(asset, index);
            ChangeTab(0);
        }

        private void ResetComboPlayback()
        {
            comboPreviewTime = 0f;
            playing = false;
        }

        private void DrawComboAnimation(Rect view)
        {
            ComboLink edge = SelectedComboEdge;
            if (edge?.From != null && edge.To != null && DrawBranchAnimation(view, edge))
                return;

            if (comboNode != null)
            {
                float frame = comboPreviewTime * comboNode.Rate;
                int index = comboNode.MotionAt(frame);
                if (index >= 0)
                {
                    ActionMotion motion = comboNode.Motions[index];
                    preview.Draw(view, PreviewModel, motion.Clip, ActionClipTime(comboNode, index, frame),
                        firstStart: motion.ClipTime(0f), baseOffset: ActionRootOffset(comboNode, index), editable: comboNode);
                    return;
                }
            }
            EditorGUI.HelpBox(view, "选择含动画的动作节点。", MessageType.Info);
        }

        private bool DrawBranchAnimation(Rect view, ComboLink edge)
        {
            float lead = ComboLead(edge);
            float delta = comboPreviewTime - lead;
            float fromFrame = edge.OpenFrame + delta * edge.From.Rate;
            int fromIndex = edge.From.MotionAt(edge.OpenFrame);
            int toIndex = edge.To.MotionAt(Mathf.Max(0f, delta * edge.To.Rate));
            if (fromIndex < 0 || toIndex < 0)
                return false;

            ActionMotion prior = edge.From.Motions[fromIndex];
            ActionMotion next = edge.To.Motions[toIndex];
            float fade = profile?.FadeBetween(prior.Clip, next.Clip, next.Fade) ?? next.Fade;
            float weight = delta < 0f ? 0f : fade <= 0f ? 1f : Mathf.Clamp01(delta / fade);
            preview.Draw(view, PreviewModel, prior.Clip, ActionClipTime(edge.From, fromIndex, fromFrame),
                next.Clip, ActionClipTime(edge.To, toIndex, Mathf.Max(0f, delta * edge.To.Rate)), weight,
                firstExit: ActionClipTime(edge.From, fromIndex, edge.OpenFrame), firstStart: prior.ClipTime(0f),
                secondStart: next.ClipTime(0f), baseOffset: ActionRootOffset(edge.From, fromIndex), editable: edge.From);
            return true;
        }

        private Vector3 ActionRootOffset(DuelActionAsset move, int index)
        {
            Vector3 sum = Vector3.zero;
            int end = move.Motions[index].Frame;
            for (int i = 0; i < move.Motions.Count; i++)
            {
                ActionMotion segment = move.Motions[i];
                if (segment.Clip != null && segment.Frame < end && move.MotionAt(segment.Frame) == i)
                    sum += preview.RootDelta(segment.Clip, segment.ClipTime(0f), segment.ClipTime(1f), move);
            }
            return sum;
        }

        private static float ActionClipTime(DuelActionAsset move, int index, float frame)
        {
            ActionMotion motion = move.Motions[index];
            return motion.ClipTime(Mathf.Clamp01((frame - motion.Frame) / (move.MotionEnd(index) - motion.Frame)));
        }
    }
}
