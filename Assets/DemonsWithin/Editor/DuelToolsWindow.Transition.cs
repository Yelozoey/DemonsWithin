using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public sealed partial class DuelToolsWindow
    {
        private enum PreviewRole { Player, Demon }

        [SerializeField] private PreviewRole previewRole;
        [SerializeField] private AnimationClip transitionFrom, transitionTo;
        private GameObject animationModel;
        private DuelLoadout roleLoadout;
        private DuelCombo roleCombo;
        private DuelCombo roleAdditionalCombos;
        private readonly AdvancedDropdownState[] clipMenus = { new(), new() };
        private float dragBlend, dragStart, dragPixelsPerSecond;
        private bool draggingBlend, changedBlend;
        private static readonly Color SourceColor = new(0.29f, 0.45f, 0.61f);
        private static readonly Color DestinationColor = new(0.24f, 0.56f, 0.53f);
        private static readonly Color PlayHeadColor = new(0.99f, 0.76f, 0.38f);

        private float BlendLength => transitionFrom != null && transitionTo != null && profile != null
            ? Mathf.Clamp(profile.FadeBetween(transitionFrom, transitionTo, profile.LocomotionFade),
                0f, Mathf.Min(transitionFrom.length, transitionTo.length)) : 0f;

        private float TransitionEnd => transitionFrom.length + transitionTo.length - BlendLength;

        private void SetPreviewRole(PreviewRole role)
        {
            previewRole = role;
            animationModel = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/DemonsWithin/Prefabs/{role}.prefab");
            DuelLoadout loadout = animationModel != null && animationModel.TryGetComponent(out DuelFighter fighter)
                ? new SerializedObject(fighter).FindProperty("loadout")?.objectReferenceValue as DuelLoadout
                : null;
            roleLoadout = loadout != null ? loadout : DuelLoadout.Default;
            profile = roleLoadout?.Animation;
            roleCombo = roleLoadout?.Combo;
            roleAdditionalCombos = roleLoadout?.AdditionalCombos;
            if (DuelAnimationPreview.ResolveAnimator(animationModel) == null)
                animationModel = profile?.PreviewModel;
            if (profile != null)
            {
                AnimationFade initial = profile.Fades.FirstOrDefault(rule => rule.From != null && rule.To != null);
                transitionFrom ??= initial?.From ?? profile.Idle;
                transitionTo ??= initial?.To ?? profile.Forward?.Loop;
            }
            playing = false;
        }

        private void DrawRole()
        {
            var role = (PreviewRole)EditorGUILayout.EnumPopup("角色", previewRole);
            if (role != previewRole)
                SetPreviewRole(role);
        }

        private GameObject PreviewModel => animationModel != null ? animationModel : profile?.PreviewModel;

        private void DrawTransitionWorkspace(Rect left, Rect right)
        {
            using (new GUILayout.AreaScope(left))
            {
                DrawRole();
                DrawClipPicker("前一片段", transitionFrom, 0, clip => transitionFrom = clip);
                DrawClipPicker("后一片段", transitionTo, 1, clip => transitionTo = clip);
                GUILayout.Space(12f);
                if (transitionFrom != null && transitionTo != null)
                {
                    DrawBlendTimeline();
                    GUILayout.Space(8f);
                    PlaybackButton(ref blendTime, TransitionEnd);
                }
            }

            if (transitionFrom == null || transitionTo == null)
            {
                EditorGUI.HelpBox(right, "选择两个片段。", MessageType.Info);
                return;
            }

            float length = BlendLength;
            float cross = transitionFrom.length - length;
            float nextTime = Mathf.Max(0f, blendTime - cross);
            float weight = blendTime < cross ? 0f : length <= 0f ? 1f : Mathf.Clamp01(nextTime / length);
            preview.Draw(right, PreviewModel, transitionFrom, Mathf.Min(blendTime, transitionFrom.length),
                transitionTo, nextTime, weight, firstExit: cross);
        }

        private void DrawClipPicker(string label, AnimationClip clip, int slot, Action<AnimationClip> select)
        {
            Rect field = EditorGUILayout.GetControlRect();
            Rect button = EditorGUI.PrefixLabel(field, new GUIContent(label));
            if (!EditorGUI.DropdownButton(button, new GUIContent(NameOf(clip)), FocusType.Keyboard))
                return;

            var dropdown = new AnimationClipMenu(clipMenus[slot], chosen =>
            {
                select(chosen);
                blendTime = 0f;
                playing = false;
                Repaint();
            });
            dropdown.Show(button);
        }

        private void DrawBlendTimeline()
        {
            Rect section = GUILayoutUtility.GetRect(20f, 128f, GUILayout.ExpandWidth(true));
            float left = section.x + 9f;
            float width = Mathf.Max(10f, section.width - 18f);
            float total = Mathf.Max(0.001f, transitionFrom.length + transitionTo.length);
            float pixelsPerSecond = width / total;
            float blend = BlendLength;
            float firstEnd = left + transitionFrom.length * pixelsPerSecond;
            float secondStart = firstEnd - blend * pixelsPerSecond;
            Rect first = new(left, section.y + 33f, firstEnd - left, 24f);
            Rect second = new(secondStart, section.y + 68f, transitionTo.length * pixelsPerSecond, 24f);
            Rect ruler = new(left, section.y + 6f, width, 22f);
            Rect blendBar = new(secondStart, first.y, Mathf.Max(1f, firstEnd - secondStart), second.yMax - first.y);

            EditorGUI.DrawRect(section, new Color(0.12f, 0.14f, 0.17f));
            for (int i = 0; i <= 4; i++)
            {
                float moment = total * i / 4f;
                float x = left + moment * pixelsPerSecond;
                EditorGUI.DrawRect(new Rect(x, ruler.yMax, 1f, 67f), new Color(1f, 1f, 1f, 0.08f));
                if (i < 4)
                    GUI.Label(new Rect(x, ruler.y, 47f, 18f), $"{moment:0.##}s", EditorStyles.miniLabel);
            }
            DrawClipBlock(first, transitionFrom.name, SourceColor);
            DrawClipBlock(second, transitionTo.name, DestinationColor);
            EditorGUI.DrawRect(blendBar, new Color(0.65f, 0.88f, 1f, 0.22f));
            EditorGUI.DrawRect(new Rect(second.x, second.y, 3f, second.height), new Color(0.69f, 0.88f, 1f));
            float playX = left + blendTime * pixelsPerSecond;
            EditorGUI.DrawRect(new Rect(playX, ruler.yMax - 5f, 2f, second.yMax - ruler.yMax + 5f), PlayHeadColor);
            GUI.Label(new Rect(left, section.y + 99f, width, 20f),
                $"混合 {blend:0.00}s   ·   拖动后一片段", EditorStyles.miniLabel);

            Rect target = new(second.x - 4f, second.y, second.width + 8f, second.height);
            EditorGUIUtility.AddCursorRect(target, MouseCursor.ResizeHorizontal);
            int control = GUIUtility.GetControlID(FocusType.Passive, section);
            Event pointer = Event.current;
            EventType phase = pointer.GetTypeForControl(control);
            if (phase == EventType.MouseDown && pointer.button == 0 && (ruler.Contains(pointer.mousePosition) || target.Contains(pointer.mousePosition)))
            {
                GUIUtility.hotControl = control;
                draggingBlend = target.Contains(pointer.mousePosition);
                changedBlend = false;
                dragBlend = blend;
                dragStart = pointer.mousePosition.x;
                dragPixelsPerSecond = pixelsPerSecond;
                playing = false;
                if (!draggingBlend)
                    ScrubTransition(pointer.mousePosition.x, left, pixelsPerSecond);
                pointer.Use();
            }
            else if (phase == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                if (draggingBlend)
                {
                    float next = dragBlend - (pointer.mousePosition.x - dragStart) / dragPixelsPerSecond;
                    SaveTransitionLength(next);
                    blendTime = transitionFrom.length - BlendLength;
                }
                else
                    ScrubTransition(pointer.mousePosition.x, left, pixelsPerSecond);
                pointer.Use();
                Repaint();
            }
            else if (phase == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                if (changedBlend)
                    AssetDatabase.SaveAssetIfDirty(profile);
                pointer.Use();
            }
        }

        private static void DrawClipBlock(Rect rect, string title, Color fill)
        {
            EditorGUI.DrawRect(rect, fill);
            GUI.Label(new Rect(rect.x + 4f, rect.y, Mathf.Max(0f, rect.width - 8f), rect.height),
                new GUIContent(title, title), EditorStyles.miniLabel);
        }

        private void ScrubTransition(float pointer, float left, float pixelsPerSecond) =>
            blendTime = Mathf.Clamp((pointer - left) / pixelsPerSecond, 0f, TransitionEnd);

        private void SaveTransitionLength(float seconds)
        {
            if (profile == null)
                return;
            seconds = Mathf.Clamp(seconds, 0f, Mathf.Min(transitionFrom.length, transitionTo.length));
            if (!changedBlend)
            {
                Undo.RegisterCompleteObjectUndo(profile, "调整动画混合时间");
                changedBlend = true;
            }
            AnimationFade rule = profile.Fades.Find(candidate => candidate.From == transitionFrom && candidate.To == transitionTo);
            if (rule == null)
            {
                rule = new AnimationFade { From = transitionFrom, To = transitionTo };
                profile.Fades.Add(rule);
            }
            rule.Seconds = seconds;
            EditorUtility.SetDirty(profile);
        }

        private sealed class AnimationClipMenu : AdvancedDropdown
        {
            private readonly Action<AnimationClip> selected;
            private const string RootPath = "Assets/Lucy/Animation/";

            public AnimationClipMenu(AdvancedDropdownState state, Action<AnimationClip> selected) : base(state)
            {
                this.selected = selected;
                minimumSize = new Vector2(280f, 360f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("选择动画片段");
                var sources = AssetDatabase.GetAllAssetPaths()
                    .Where(path => path.StartsWith(RootPath, StringComparison.Ordinal) &&
                        path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .GroupBy(Category);
                int id = 1;
                foreach (var group in sources.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    var category = new AdvancedDropdownItem(group.Key) { id = id++ };
                    root.AddChild(category);
                    foreach (string path in group)
                        category.AddChild(new ClipItem(path) { id = id++ });
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is ClipItem entry)
                {
                    AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(entry.Path).OfType<AnimationClip>()
                        .FirstOrDefault(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal));
                    if (clip != null)
                        selected(clip);
                }
            }

            private static string Category(string path)
            {
                string local = path[RootPath.Length..];
                if (local.StartsWith("Battle/", StringComparison.Ordinal))
                    return Path.GetFileName(local).StartsWith("Lucy_Air_", StringComparison.Ordinal) ? "空中攻击" : "地面战斗";
                if (local.StartsWith("Dodge/", StringComparison.Ordinal)) return "闪避";
                if (local.StartsWith("Hit/", StringComparison.Ordinal)) return "受击";
                if (local.StartsWith("Die/", StringComparison.Ordinal)) return "倒地";
                if (local.StartsWith("Execution/", StringComparison.Ordinal)) return "双人处决";
                return "移动与待机";
            }

            private sealed class ClipItem : AdvancedDropdownItem
            {
                public string Path { get; }

                public ClipItem(string path) : base(System.IO.Path.GetFileNameWithoutExtension(path).Replace("Lucy_", "")) =>
                    Path = path;
            }
        }
    }
}
