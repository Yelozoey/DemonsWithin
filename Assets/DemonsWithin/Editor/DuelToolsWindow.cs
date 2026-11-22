using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed partial class DuelToolsWindow : EditorWindow
    {
        private const string Combat = "Assets/DemonsWithin/Combat/";
        private static readonly string[] Tabs = { "连招配置", "动画过渡", "动作时间轴" };
        [SerializeField] private DuelCombo combo;
        [SerializeField] private DuelAnimationProfile profile;
        [SerializeField] private DuelActionAsset action;
        [SerializeField] private int tab;

        private readonly DuelAnimationPreview preview = new();
        private Vector2 scroll;
        private int linkIndex;
        private float blendTime, actionFrame;
        private bool playing;
        private double previousTime;

        [MenuItem("Tools/Demons Within/连招配置", priority = 100)]
        private static void OpenCombo() => Open(0);
        [MenuItem("Tools/Demons Within/动画过渡预览", priority = 101)]
        private static void OpenTransitions() => Open(1);
        [MenuItem("Tools/Demons Within/动作编辑器", priority = 102)]
        private static void OpenAction() => Open(2);

        private static void Open(int tab)
        {
            var window = GetWindow<DuelToolsWindow>("心魔 · 战斗工具");
            window.ChangeTab(tab);
            if (tab == 2 && Selection.activeObject is DuelActionAsset selected)
                window.SetAction(selected);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(740, 530);
            if (combo == null)
                combo = AssetDatabase.LoadAssetAtPath<DuelCombo>(Combat + "LucyBasicCombo.asset");
            if (profile == null)
                profile = AssetDatabase.LoadAssetAtPath<DuelAnimationProfile>(Combat + "LucyAnimation.asset");
            if (action == null)
                action = AssetDatabase.LoadAssetAtPath<DuelActionAsset>(Combat + "Actions/Quick.asset");
            SetPreviewRole(previewRole);
            Undo.undoRedoPerformed += Repaint;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += ResetPreview;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= Repaint;
            EditorApplication.playModeStateChanged -= ResetPreview;
            preview.Dispose();
        }

        private void ResetPreview(PlayModeStateChange state)
        {
            preview.Dispose();
            if (state is PlayModeStateChange.EnteredEditMode or PlayModeStateChange.EnteredPlayMode)
            {
                SetPreviewRole(previewRole);
                Repaint();
            }
        }

        private void OnGUI()
        {
            int next = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(28));
            if (next != tab)
                ChangeTab(next);
            if (tab == 0)
            {
                DrawComboWorkspace();
                return;
            }

            float leftWidth = Mathf.Clamp(position.width * 0.44f, 325f, 510f);
            Rect left = new(7f, 35f, leftWidth, position.height - 43f);
            Rect right = new(left.xMax + 9f, left.y, position.width - left.xMax - 16f, left.height);
            if (tab == 1)
                DrawTransitionWorkspace(left, right);
            else
                DrawActionWorkspace(left, right);
        }

        private void ChangeTab(int target)
        {
            tab = target;
            scroll = Vector2.zero;
            playing = false;
        }

        private static void DrawComboCondition(SerializedProperty link, ComboCondition active)
        {
            int selection = EditorGUILayout.Popup("结果条件", (int)active, ComboConditions);
            if (selection == (int)active)
                return;
            SetInt(link, "Condition", selection);
            link.FindPropertyRelative("RequireHit").boolValue = false;
        }

        private static readonly string[] ComboConditions = { "任意", "命中", "被格挡", "落空", "被闪避" };

        private static string ConditionName(ComboCondition condition) =>
            ComboConditions[Mathf.Clamp((int)condition, 0, ComboConditions.Length - 1)];

        private void PlaybackButton(ref float value, float end)
        {
            if (!GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(70f)))
                return;
            if (value >= end)
                value = 0f;
            playing = !playing;
            previousTime = EditorApplication.timeSinceStartup;
        }

        private void Tick()
        {
            if (!playing)
                return;
            if (preview.EditingBox)
            {
                playing = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - previousTime);
            previousTime = now;
            if (tab == 0)
            {
                float end = ComboPreviewDuration;
                comboPreviewTime = Mathf.Min(comboPreviewTime + delta, end);
                playing = comboPreviewTime < end;
            }
            else if (tab == 1 && transitionFrom != null && transitionTo != null)
            {
                float end = TransitionEnd;
                blendTime = Mathf.Min(blendTime + delta, end);
                playing = blendTime < end;
            }
            else if (tab == 2 && action != null)
            {
                actionFrame = Mathf.Min(actionFrame + delta * action.Rate, action.Length);
                playing = actionFrame < action.Length;
            }
            else
                playing = false;
            Repaint();
        }

        private static void Segment(Rect row, int start, int end, int frames, Color color, string label)
        {
            float x = ToX(row, start, frames);
            float width = ToX(row, end, frames) - x;
            if (width <= 0f) return;
            Rect block = new(x, row.y + 1, width, row.height - 2);
            EditorGUI.DrawRect(block, color);
            if (width > 36f)
                GUI.Label(block, label, EditorStyles.centeredGreyMiniLabel);
        }

        private static float ToX(Rect row, float frame, int frames) => row.x + row.width * Mathf.Clamp01(frame / frames);
        private static string NameOf(Object value) => value == null ? "未绑定" : value.name;
        private static void SetInt(SerializedProperty root, string name, int value) => root.FindPropertyRelative(name).intValue = value;
        private static void SetRef(SerializedProperty root, string name, Object value) => root.FindPropertyRelative(name).objectReferenceValue = value;
        private static void Field(SerializedProperty root, string name, string label) =>
            EditorGUILayout.PropertyField(root.FindPropertyRelative(name), new GUIContent(label));
        private static void Field(SerializedObject data, string name, string label, bool children = false) =>
            EditorGUILayout.PropertyField(data.FindProperty(name), new GUIContent(label), children);
    }
}
