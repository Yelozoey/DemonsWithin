using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed partial class DuelToolsWindow
    {
        private static readonly string[] Tracks =
            { "战斗阶段", "动画片段", "事件", "音效", "特效", "转向窗口", "输入缓存", "派生窗口" };
        private static readonly Color[] TrackColors =
        {
            new(0.42f, 0.47f, 0.66f), new(0.23f, 0.54f, 0.60f), new(0.62f, 0.54f, 0.77f),
            new(0.36f, 0.70f, 0.51f), new(0.80f, 0.55f, 0.31f)
        };
        private int selectedIndex = -1;
        private int selectedTrack = -1;
        private bool showAttackBox = true;
        private bool showActionParameters;
        private bool turnDragStart, turnDragEdited;
        private BoxGizmoMode boxMode;
        private static readonly int[] TrackOrder = { 0, 5, 6, 7, 1, 2, 3, 4 };
        private static readonly string[] BoxModes = { "移动", "改尺寸", "旋转" };
        private static readonly Color TurnColor = new(0.22f, 0.57f, 0.62f);
        private static readonly Color BufferColor = new(0.38f, 0.43f, 0.66f);
        private static readonly Color BranchColor = new(0.29f, 0.60f, 0.51f);
        private readonly List<(DuelCombo asset, int index)> actionRoutes = new();
        private int selectedRoute;
        private DuelCombo catalogCombo;

        private void DrawActionWorkspace(Rect left, Rect right)
        {
            using (new GUILayout.AreaScope(left))
            {
                DrawRole();
                using (new EditorGUILayout.HorizontalScope())
                {
                    var choice = (DuelActionAsset)EditorGUILayout.ObjectField("动作", action, typeof(DuelActionAsset), false);
                    if (choice != action)
                        SetAction(choice);
                    if (GUILayout.Button("Lucy 动作库", GUILayout.Width(95f)))
                        ChooseLucyAction();
                }

                if (action != null)
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawActionTracks();
                    EditorGUILayout.EndScrollView();
                }
            }

            if (action == null)
            {
                EditorGUI.HelpBox(right, "选择动作后可预览。", MessageType.Info);
                return;
            }

            if (action.Attack)
            {
                Rect tools = new(right.x, right.y, right.width, 22f);
                showAttackBox = GUI.Toggle(new Rect(tools.x, tools.y, 67f, tools.height), showAttackBox, "攻击盒");
                if (showAttackBox)
                    boxMode = (BoxGizmoMode)GUI.Toolbar(new Rect(tools.x + 69f, tools.y, 189f, tools.height), (int)boxMode, BoxModes);
                right.yMin += 25f;
            }
            DrawActionPreview(right);
        }

        private void DrawActionTracks()
        {
            var data = new SerializedObject(action);
            using (new EditorGUILayout.HorizontalScope())
            {
                PlaybackButton(ref actionFrame, action.Length);
                EditorGUILayout.LabelField($"{actionFrame:0.#} / {action.Length} 帧", EditorStyles.miniLabel);
            }
            ComboLink route = PickActionRoute();
            DrawTimeline(route);
            DrawAddButtons(data);
            DrawSelectedKey(data);
            showActionParameters = EditorGUILayout.Foldout(showActionParameters, "动作参数", true);
            if (showActionParameters)
                DrawTiming(data);
            if (data.ApplyModifiedProperties())
                Repaint();
        }

        private ComboLink PickActionRoute()
        {
            actionRoutes.Clear();
            CollectRoutes(roleCombo);
            if (roleAdditionalCombos != roleCombo)
                CollectRoutes(roleAdditionalCombos);
            if (roleAdditionalCombos == null && AssetDatabase.IsSubAsset(action))
            {
                if (catalogCombo == null)
                    catalogCombo = LucyCombatLibrary.Load()?.Combo;
                if (catalogCombo != roleCombo)
                    CollectRoutes(catalogCombo);
            }
            if (combo != roleCombo && combo != roleAdditionalCombos && combo != catalogCombo)
                CollectRoutes(combo);
            if (actionRoutes.Count == 0)
                return null;

            var labels = new string[actionRoutes.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                var item = actionRoutes[i];
                ComboLink link = item.asset.Links[item.index];
                labels[i] = $"{DuelCombo.InputLabel(link.Input)} · {ConditionName(link.RequiredResult)} → {NameOf(link.To)}";
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                selectedRoute = EditorGUILayout.Popup(Mathf.Clamp(selectedRoute, 0, labels.Length - 1), labels);
                var current = actionRoutes[selectedRoute];
                if (GUILayout.Button("编辑派生", GUILayout.Width(72f)))
                {
                    FocusComboBranch(current.asset, current.index);
                }
                return current.asset.Links[current.index];
            }
        }

        private void CollectRoutes(DuelCombo source)
        {
            if (source == null)
                return;
            for (int i = 0; i < source.Links.Count; i++)
                if (source.Links[i]?.From == action)
                    actionRoutes.Add((source, i));
        }

        private void ChooseLucyAction()
        {
            LucyCombatLibrary library = LucyCombatLibrary.Load();
            var menu = new GenericMenu();
            if (library == null)
                menu.AddDisabledItem(new GUIContent("Lucy 动作库未生成"));
            else
                foreach (LucyCombatEntry entry in library.Entries)
                {
                    string label = $"{LucyCombatEntry.Label(entry.Kind)}/{entry.Take}/{entry.Source}";
                    menu.AddItem(new GUIContent(label), action == entry.Action, () => SetAction(entry.Action));
                }
            menu.ShowAsContext();
        }

        private void DrawTiming(SerializedObject data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                float previous = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 58f;
                Field(data, "Action", "动作");
                Field(data, "FrameRate", "帧率");
                Field(data, "Duration", "总帧数");
                EditorGUIUtility.labelWidth = previous;
            }
            Field(data, "Attack", "攻击");
            if (data.FindProperty("Attack").boolValue)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    float previous = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 76f;
                    Field(data, "HitStart", "命中起始");
                    Field(data, "HitEnd", "命中截止");
                    EditorGUIUtility.labelWidth = previous;
                }

                EditorGUILayout.LabelField(
                    $"前摇 {action.HitStart}f  ·  命中 {action.HitEnd - action.HitStart}f  ·  后摇 {action.Length - action.HitEnd}f",
                    EditorStyles.miniBoldLabel);
                if (action.HitStart >= action.HitEnd || action.HitEnd > action.Length)
                    EditorGUILayout.HelpBox("命中起始需早于截止，且截止须在总帧数内。", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    float previous = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 55f;
                    Field(data, "Reach", "距离");
                    Field(data, "HalfAngle", "半角");
                    Field(data, "Damage", "伤害");
                    EditorGUIUtility.labelWidth = previous;
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Field(data, "DefenseStart", "防御起始");
                    Field(data, "DefenseEnd", "防御截止");
                }
            }
            if (action.CanSteer)
            {
                Vector2Int range = action.TurnWindow;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"转向 {range.x}–{range.y} 帧");
                    if (action.CustomTurnWindow && GUILayout.Button("恢复自动", GUILayout.Width(82f)))
                        data.FindProperty("CustomTurnWindow").boolValue = false;
                }
            }
            if (action.IsDodge)
            {
                Field(data, "DodgeDistance", "胶囊闪避距离（米）");
                Field(data, "DodgeTravelSeconds", "闪避位移时长（秒）");
            }
            else
            {
                if (action.Attack)
                    Field(data, "Space", "起手空间");
                if (action.Space == ActionSpace.GroundLaunch)
                {
                    Field(data, "LaunchHeight", "起跳高度曲线");
                    Field(data, "TargetLiftSpeed", "命中挑空速度");
                }
                else if (action.Space == ActionSpace.Air)
                    Field(data, "AirPoseOffset", "空中姿势高度校正");
                Field(data, "RootMotionScale", "根运动位移倍率");
                Field(data, "ApplyRootYaw", "应用根旋转");
                Field(data, "ApplyRootHeight", "应用垂直根位移");
            }
        }

        private void DrawTimeline(ComboLink route)
        {
            const float header = 68;
            Rect grid = GUILayoutUtility.GetRect(10, route == null ? 181 : 233, GUILayout.ExpandWidth(true));
            Rect ruler = new(grid.x + header, grid.y, grid.width - header - 8, 20);
            int length = action.Length;
            int interval = Mathf.Max(1, Mathf.CeilToInt(length / 10f));
            for (int frame = 0; frame <= length; frame += interval)
            {
                float x = ToX(ruler, frame, length);
                EditorGUI.DrawRect(new Rect(x, grid.y, 1, grid.height), new Color(0.38f, 0.40f, 0.45f, 0.46f));
                GUI.Label(new Rect(x + 2, ruler.y, 36, 18), frame.ToString(), EditorStyles.miniLabel);
            }

            int rowIndex = 0;
            foreach (int track in TrackOrder)
            {
                if (track >= 6 && route == null)
                    continue;
                float y = grid.y + 23 + rowIndex++ * 26;
                GUI.Label(new Rect(grid.x + 4, y, header - 4, 23), Tracks[track]);
                Rect row = new(ruler.x, y, ruler.width, 23);
                EditorGUI.DrawRect(row, new Color(0.15f, 0.17f, 0.20f, 0.84f));
                if (track == 0)
                    DrawPhases(row, length);
                else if (track == 1)
                    DrawMotions(row, length);
                else if (track == 5)
                    DrawTurnWindow(row, length);
                else if (track == 6)
                    Segment(row, route.BufferFrom, route.InputCloseFrame, length, BufferColor,
                        $"{route.BufferFrom}–{route.InputCloseFrame}f");
                else if (track == 7)
                    Segment(row, route.OpenFrame, route.CloseFrame, length, BranchColor, $"{route.OpenFrame}–{route.CloseFrame}f");
                else
                    DrawCues(row, track, length);
            }

            float at = ToX(ruler, actionFrame, length);
            EditorGUI.DrawRect(new Rect(at, grid.y, 2, grid.height), PlayHeadColor);
            Event pointer = Event.current;
            Rect scrub = ruler;
            if ((pointer.type == EventType.MouseDown || pointer.type == EventType.MouseDrag) &&
                pointer.button == 0 && scrub.Contains(pointer.mousePosition))
            {
                actionFrame = Mathf.Round(Mathf.Clamp01((pointer.mousePosition.x - ruler.x) / ruler.width) * length);
                playing = false;
                pointer.Use();
                Repaint();
            }
        }

        private void DrawTurnWindow(Rect row, int length)
        {
            if (!action.CanSteer)
            {
                GUI.Label(row, "受击锁定", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Vector2Int window = action.TurnWindow;
            Segment(row, window.x, window.y, length, TurnColor, $"{window.x}–{window.y}f");
            float start = ToX(row, window.x, length);
            float end = ToX(row, window.y, length);
            Rect left = new(start - 3f, row.y + 3f, 6f, row.height - 6f);
            Rect right = new(end - 3f, row.y + 3f, 6f, row.height - 6f);
            EditorGUI.DrawRect(left, new Color(0.53f, 0.87f, 0.91f));
            EditorGUI.DrawRect(right, new Color(0.53f, 0.87f, 0.91f));
            EditorGUIUtility.AddCursorRect(left, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(right, MouseCursor.ResizeHorizontal);

            int control = GUIUtility.GetControlID(FocusType.Passive, row);
            Event pointer = Event.current;
            EventType phase = pointer.GetTypeForControl(control);
            if (phase == EventType.MouseDown && pointer.button == 0 &&
                (left.Contains(pointer.mousePosition) || right.Contains(pointer.mousePosition)))
            {
                GUIUtility.hotControl = control;
                turnDragStart = left.Contains(pointer.mousePosition) && !right.Contains(pointer.mousePosition);
                turnDragEdited = false;
                playing = false;
                pointer.Use();
            }
            else if (phase == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                if (!turnDragEdited)
                {
                    turnDragEdited = true;
                    Undo.RegisterCompleteObjectUndo(action, "调整动作转向窗口");
                }
                int frame = Mathf.RoundToInt(Mathf.Clamp01((pointer.mousePosition.x - row.x) / row.width) * length);
                action.CustomTurnWindow = true;
                action.TurnStart = turnDragStart ? Mathf.Min(frame, window.y) : window.x;
                action.TurnEnd = turnDragStart ? window.y : Mathf.Max(frame, window.x);
                actionFrame = frame;
                EditorUtility.SetDirty(action);
                pointer.Use();
                Repaint();
            }
            else if (phase == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                if (turnDragEdited)
                    AssetDatabase.SaveAssetIfDirty(action);
                pointer.Use();
            }
        }

        private void DrawPhases(Rect row, int length)
        {
            if (action.Attack)
            {
                Segment(row, 0, action.HitStart, length, TrackColors[0], "前摇");
                Segment(row, action.HitStart, action.HitEnd, length, new Color(0.73f, 0.35f, 0.34f), "命中");
                Segment(row, action.HitEnd, length, length, new Color(0.40f, 0.43f, 0.49f), "后摇");
            }
            else
            {
                Segment(row, action.DefenseStart, action.DefenseEnd, length, TrackColors[3], "防御窗口");
            }
        }

        private void DrawMotions(Rect row, int length)
        {
            for (int i = 0; i < action.Motions.Count; i++)
            {
                ActionMotion motion = action.Motions[i];
                float x = ToX(row, motion.Frame, length);
                float end = ToX(row, action.MotionEnd(i), length);
                Rect block = new(x, row.y + 2, Mathf.Max(10, end - x), row.height - 4);
                if (DrawMarker(block, 1, i, NameOf(motion.Clip), motion.Frame))
                    SelectKey(1, i, motion.Frame);
            }
        }

        private void DrawCues(Rect row, int track, int length)
        {
            for (int i = 0; i < action.Cues.Count; i++)
            {
                ActionCue cue = action.Cues[i];
                if ((int)cue.Track + 2 != track)
                    continue;
                float x = ToX(row, cue.Frame, length);
                Rect block = new(x - 4, row.y + 2, 15, row.height - 4);
                if (DrawMarker(block, track, i, "◆", cue.Frame))
                    SelectKey(track, i, cue.Frame);
            }
        }

        private bool DrawMarker(Rect rect, int track, int index, string title, int frame)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = selectedTrack == track && selectedIndex == index ? Color.cyan : TrackColors[track];
            bool selected = GUI.Button(rect, new GUIContent(title, $"{Tracks[track]} · {frame}f"), EditorStyles.miniButton);
            GUI.backgroundColor = previous;
            return selected;
        }

        private void DrawAddButtons(SerializedObject data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 动画片段"))
                {
                    SerializedProperty motions = data.FindProperty("Motions");
                    int index = motions.arraySize++;
                    SerializedProperty motion = motions.GetArrayElementAtIndex(index);
                    SetRef(motion, "Clip", null);
                    SetInt(motion, "Frame", Mathf.Min(action.Length - 1, Mathf.RoundToInt(actionFrame)));
                    motion.FindPropertyRelative("ClipIn").floatValue = 0f;
                    motion.FindPropertyRelative("ClipOut").floatValue = 1f;
                    motion.FindPropertyRelative("Fade").floatValue = 0.08f;
                    SelectKey(1, index, actionFrame);
                }
                if (GUILayout.Button("+ 事件")) AddCue(data, ActionTrack.Event);
                if (GUILayout.Button("+ 音效")) AddCue(data, ActionTrack.Audio);
                if (GUILayout.Button("+ 特效")) AddCue(data, ActionTrack.Effect);
            }
        }

        private void AddCue(SerializedObject data, ActionTrack track)
        {
            SerializedProperty cues = data.FindProperty("Cues");
            int index = cues.arraySize++;
            SerializedProperty cue = cues.GetArrayElementAtIndex(index);
            SetInt(cue, "Frame", Mathf.RoundToInt(actionFrame));
            SetInt(cue, "Track", (int)track);
            SetRef(cue, "Audio", null);
            SetRef(cue, "Effect", null);
            cue.FindPropertyRelative("Event").stringValue = string.Empty;
            cue.FindPropertyRelative("LocalOffset").vector3Value = Vector3.up;
            cue.FindPropertyRelative("EffectLifetime").floatValue = 3f;
            SelectKey((int)track + 2, index, actionFrame);
        }

        private void DrawSelectedKey(SerializedObject data)
        {
            if (selectedTrack < 1 || selectedIndex < 0)
                return;
            SerializedProperty array = data.FindProperty(selectedTrack == 1 ? "Motions" : "Cues");
            if (selectedIndex >= array.arraySize)
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty item = array.GetArrayElementAtIndex(selectedIndex);
                EditorGUILayout.LabelField($"{Tracks[selectedTrack]} · 第 {selectedIndex + 1} 项", EditorStyles.boldLabel);
                SerializedProperty frame = item.FindPropertyRelative("Frame");
                int frameLimit = selectedTrack == 1 ? action.Length - 1 : action.Length;
                frame.intValue = Mathf.Clamp(EditorGUILayout.IntField("起始帧", frame.intValue), 0, frameLimit);
                switch (selectedTrack)
                {
                    case 1:
                        Field(item, "Clip", "动画");
                        Field(item, "ClipIn", "片段入点");
                        Field(item, "ClipOut", "片段出点");
                        Field(item, "Fade", "与上一片段淡化（秒）");
                        break;
                    case 2:
                        Field(item, "Event", "事件名");
                        break;
                    case 3:
                        Field(item, "Audio", "音频");
                        Field(item, "LocalOffset", "本地位置");
                        break;
                    case 4:
                        Field(item, "Effect", "特效预制体");
                        Field(item, "LocalOffset", "本地位置");
                        Field(item, "EffectLifetime", "存续（秒）");
                        break;
                }
                if (GUILayout.Button("删除选中项", GUILayout.Width(105)))
                {
                    array.DeleteArrayElementAtIndex(selectedIndex);
                    selectedIndex = -1;
                }
            }
        }

        private void DrawActionPreview(Rect view)
        {
            int index = action.MotionAt(actionFrame);
            if (profile == null || index < 0)
            {
                EditorGUI.HelpBox(view, "选择表现配置并在动画轨添加片段。", MessageType.Info);
                return;
            }

            ActionMotion motion = action.Motions[index];
            float sample = SampleAt(index, actionFrame);
            AttackBox? hitBox = action.Attack && showAttackBox ? action.HitBox : null;
            bool hitActive = actionFrame >= action.HitStart && actionFrame < action.HitEnd;
            int previous = action.MotionAt(motion.Frame - 1f);
            if (previous >= 0)
            {
                ActionMotion prior = action.Motions[previous];
                float duration = profile.FadeBetween(prior.Clip, motion.Clip, motion.Fade);
                float elapsed = (actionFrame - motion.Frame) / action.Rate;
                if (duration > 0 && elapsed < duration)
                {
                    preview.Draw(view, PreviewModel, prior.Clip, SampleAt(previous, actionFrame), motion.Clip,
                        sample, Mathf.Clamp01(elapsed / duration), hitBox, hitActive, action, boxMode,
                        firstExit: SampleAt(previous, motion.Frame), firstStart: prior.ClipTime(0f),
                        secondStart: motion.ClipTime(0f), baseOffset: ActionRootOffset(action, previous));
                    return;
                }
            }
            preview.Draw(view, PreviewModel, motion.Clip, sample, hitBox: hitBox, hitActive: hitActive,
                editable: action, boxMode: boxMode, firstStart: motion.ClipTime(0f),
                baseOffset: ActionRootOffset(action, index));
        }

        private float SampleAt(int index, float frame) => ActionClipTime(action, index, frame);

        private void SetAction(DuelActionAsset value)
        {
            action = value;
            selectedIndex = -1;
            selectedRoute = 0;
            actionFrame = 0;
            playing = false;
        }

        private void SelectKey(int track, int index, float frame)
        {
            selectedTrack = track;
            selectedIndex = index;
            actionFrame = frame;
            playing = false;
        }
    }
}
