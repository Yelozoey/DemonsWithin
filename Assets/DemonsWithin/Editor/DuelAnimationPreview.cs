using System.Collections.Generic;
using Animancer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DemonsWithin.Editor
{
    internal sealed class DuelAnimationPreview
    {
        private PreviewRenderUtility preview;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClip[] clips = new AnimationClip[2];
        private readonly AnimationClipPlayable[] inputs = new AnimationClipPlayable[2];
        private GameObject sourceModel;
        private Animator actor;
        private PreviewGrid floor;
        private readonly Dictionary<AnimationClip, RootTrack> rootTracks = new();
        private Vector3 previewPosition;
        private Vector3 center;
        private float distance;
        private float yaw = 30f;
        private float pitch = 16f;
        private readonly AttackBoxPreviewGizmo gizmo = new();
        public bool EditingBox => gizmo.IsDragging;
        private static readonly int[] BoxEdges =
            { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

        public void Draw(Rect rect, GameObject model, AnimationClip first, float firstTime,
            AnimationClip second = null, float secondTime = 0f, float weight = 0f,
            AttackBox? hitBox = null, bool hitActive = false, DuelActionAsset editable = null,
            BoxGizmoMode boxMode = BoxGizmoMode.Move, float firstExit = -1f,
            float firstStart = 0f, float secondStart = 0f, Vector3 baseOffset = default)
        {
            if (model == null || first == null)
            {
                EditorGUI.HelpBox(rect, "分配角色和动画片段后可预览。", MessageType.Info);
                return;
            }

            Event pointer = Event.current;
            if (rect.Contains(pointer.mousePosition))
            {
                if (pointer.type == EventType.MouseDrag && (pointer.button == 1 || (pointer.alt && pointer.button == 0)))
                {
                    yaw += pointer.delta.x * 0.8f;
                    pitch = Mathf.Clamp(pitch + pointer.delta.y * 0.55f, -18f, 65f);
                    GUI.changed = true;
                    pointer.Use();
                }
                if (pointer.type == EventType.ScrollWheel)
                {
                    distance = Mathf.Clamp(distance * Mathf.Exp(pointer.delta.y * .045f), .8f, 14f);
                    GUI.changed = true;
                    pointer.Use();
                }
            }

            if (model != sourceModel || (preview != null && !graph.IsValid()))
                Create(model);
            if (!graph.IsValid())
            {
                EditorGUI.HelpBox(rect, $"角色 {model.name} 缺少有效的 Animator 与 Avatar。", MessageType.Warning);
                return;
            }

            if (pointer.type == EventType.Repaint)
            {
                Sample(0, first, firstTime);
                Sample(1, second, secondTime);
                mixer.SetInputWeight(0, second != null ? 1f - weight : 1f);
                mixer.SetInputWeight(1, second != null ? weight : 0f);
                graph.Evaluate(0f);
                previewPosition = baseOffset + RootDelta(first, firstStart, firstTime, editable);
                if (second != null)
                {
                    if (firstExit < 0f)
                        firstExit = Mathf.Max(firstStart, firstTime - (secondTime - secondStart));
                    Vector3 incoming = baseOffset + RootDelta(first, firstStart, firstExit, editable) +
                        RootDelta(second, secondStart, secondTime, editable);
                    previewPosition = Vector3.Lerp(previewPosition, incoming, weight);
                }
                actor.transform.SetPositionAndRotation(previewPosition + Vector3.up * .012f, Quaternion.identity);
                preview.BeginPreview(rect, GUIStyle.none);
                Vector3 offset = Quaternion.Euler(pitch, yaw, 0f) * (Vector3.back * distance);
                preview.camera.aspect = rect.width / Mathf.Max(1f, rect.height);
                preview.camera.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
                preview.Render(true);
                Vector3[] corners = hitBox.HasValue ? ProjectHitBox(rect.size, hitBox.Value, previewPosition) : null;
                preview.EndAndDrawPreview(rect);
                if (corners != null)
                    DrawHitBox(rect, corners, hitActive);
            }
            if (hitBox.HasValue && editable != null)
                gizmo.Draw(rect, preview.camera, editable, boxMode, previewPosition);
        }

        private Vector3[] ProjectHitBox(Vector2 size, AttackBox hitBox, Vector3 origin)
        {
            var matrix = hitBox.LocalMatrix;
            Vector3[] points = new Vector3[8];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 corner = new((i & 1) == 0 ? -0.5f : 0.5f,
                    (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                Vector3 view = preview.camera.WorldToViewportPoint(matrix.MultiplyPoint3x4(corner) + origin);
                if (view.z <= preview.camera.nearClipPlane)
                    return null;
                points[i] = new Vector3(view.x * size.x, (1f - view.y) * size.y);
            }

            return points;
        }

        private static void DrawHitBox(Rect rect, Vector3[] points, bool active)
        {
            GUI.BeginGroup(rect);
            Handles.BeginGUI();
            Color saved = Handles.color;
            Handles.color = active ? new Color(1f, 0.35f, 0.2f) : new Color(0.35f, 0.8f, 1f, 0.8f);
            for (int i = 0; i < BoxEdges.Length; i += 2)
                Handles.DrawAAPolyLine(active ? 2.6f : 1.5f, points[BoxEdges[i]], points[BoxEdges[i + 1]]);
            Handles.color = saved;
            Handles.EndGUI();
            GUI.EndGroup();
        }

        public void Dispose()
        {
            gizmo.Cancel();
            if (graph.IsValid())
                graph.Destroy();
            preview?.Cleanup();
            floor?.Dispose();
            floor = null;
            actor = null;
            preview = null;
            sourceModel = null;
            rootTracks.Clear();
            clips[0] = clips[1] = null;
            inputs[0] = inputs[1] = default;
        }

        private void Create(GameObject source)
        {
            Dispose();
            sourceModel = source;
            Animator template = ResolveAnimator(source);
            if (template == null)
                return;

            preview = new PreviewRenderUtility();
            preview.camera.fieldOfView = 35f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.095f, 0.115f, 0.145f);
            preview.camera.nearClipPlane = 0.02f;
            preview.camera.farClipPlane = 120f;
            preview.ambientColor = new Color(0.46f, 0.49f, 0.54f);
            preview.lights[0].intensity = 1.35f;
            preview.lights[0].transform.rotation = Quaternion.Euler(32f, -35f, 0f);
            preview.lights[1].intensity = 0.9f;
            preview.lights[1].transform.rotation = Quaternion.Euler(25f, 135f, 0f);

            var animator = Object.Instantiate(template);
            animator.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            preview.AddSingleGO(animator.gameObject);
            actor = animator;
            animator.runtimeAnimatorController = null;
            animator.enabled = true;
            animator.gameObject.SetActive(true);
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            floor = new PreviewGrid();
            preview.AddSingleGO(floor.Floor);

            Bounds bounds = new(Vector3.up * 0.95f, new Vector3(0.6f, 1.9f, 0.4f));
            foreach (Renderer renderer in animator.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            center = bounds.center;
            distance = Mathf.Max(1f, bounds.extents.magnitude * 3.8f);
            graph = PlayableGraph.Create("Demons Within animation preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            AnimationPlayableOutput.Create(graph, "Preview", animator).SetSourcePlayable(mixer);
            graph.Play();
        }

        internal static Animator ResolveAnimator(GameObject model)
        {
            if (model == null)
                return null;
            if (model.GetComponentInChildren<AnimancerComponent>(true) is AnimancerComponent animancer &&
                ValidAnimator(animancer.Animator))
                return animancer.Animator;
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                if (ValidAnimator(animator))
                    return animator;
            return null;
        }

        private static bool ValidAnimator(Animator animator) => animator != null && animator.avatar != null && animator.avatar.isValid;

        public Vector3 RootDelta(AnimationClip clip, float start, float end, DuelActionAsset action = null)
        {
            if (clip == null)
                return Vector3.zero;
            if (!rootTracks.TryGetValue(clip, out RootTrack track))
            {
                track = new RootTrack(clip);
                rootTracks.Add(clip, track);
            }

            Vector3 delta = track.At(Mathf.Clamp(end, 0f, clip.length)) - track.At(Mathf.Clamp(start, 0f, clip.length));
            if (action != null && !action.ApplyRootHeight)
                delta.y = 0f;
            float scale = action == null || action.IsDodge ? 1f : action.RootMotionScale;
            return delta * scale * (actor != null && actor.isHuman ? actor.humanScale : 1f);
        }

        private sealed class RootTrack
        {
            private readonly AnimationCurve x, y, z;

            public RootTrack(AnimationClip clip)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    switch (binding.propertyName)
                    {
                        case "RootT.x" when !settings.loopBlendPositionXZ: x = AnimationUtility.GetEditorCurve(clip, binding); break;
                        case "RootT.z" when !settings.loopBlendPositionXZ: z = AnimationUtility.GetEditorCurve(clip, binding); break;
                        case "RootT.y" when !settings.loopBlendPositionY: y = AnimationUtility.GetEditorCurve(clip, binding); break;
                    }
                }
            }

            public Vector3 At(float time) => new(x?.Evaluate(time) ?? 0f, y?.Evaluate(time) ?? 0f, z?.Evaluate(time) ?? 0f);
        }

        private void Sample(int index, AnimationClip clip, float time)
        {
            if (clips[index] != clip)
            {
                if (inputs[index].IsValid())
                {
                    graph.Disconnect(mixer, index);
                    graph.DestroyPlayable(inputs[index]);
                    inputs[index] = default;
                }

                clips[index] = clip;
                if (clip != null)
                {
                    inputs[index] = AnimationClipPlayable.Create(graph, clip);
                    inputs[index].SetSpeed(0d);
                    graph.Connect(inputs[index], 0, mixer, index);
                }
            }
            if (inputs[index].IsValid())
                inputs[index].SetTime(Mathf.Clamp(time, 0f, clip.length));
        }
    }
}
