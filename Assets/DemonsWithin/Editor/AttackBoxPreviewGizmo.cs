using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    internal enum BoxGizmoMode { Move, Size, Yaw }

    internal sealed class AttackBoxPreviewGizmo
    {
        private static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] Colors =
        {
            new(.98f, .37f, .34f), new(.48f, .87f, .39f), new(.31f, .63f, .99f)
        };

        private DuelActionAsset editing;
        private AttackBox initial;
        private BoxGizmoMode dragMode;
        private Vector2 origin, screenDirection;
        private Vector3 direction;
        private Vector3 frameOrigin;
        private float dragAngle;
        private int activeAxis;
        private bool changed;

        public bool IsDragging => editing != null;

        public void Cancel() => editing = null;

        public void Draw(Rect view, Camera camera, DuelActionAsset action, BoxGizmoMode mode, Vector3 previewOrigin)
        {
            AttackBox local = action.HitBox;
            AttackBox box = new(local.Center + previewOrigin, local.Size, local.Yaw);
            Event pointer = Event.current;
            Vector2 mouse = pointer.mousePosition;
            int control = GUIUtility.GetControlID(FocusType.Passive, view);
            EventType phase = pointer.GetTypeForControl(control);
            int candidate = -1;
            float nearest = 11f;

            if (mode == BoxGizmoMode.Yaw)
            {
                if (DrawRing(view, camera, box, mouse) < nearest)
                    candidate = 0;
            }
            else
                candidate = DrawAxes(view, camera, box, mode, mouse, nearest);

            if (phase == EventType.MouseDown && pointer.button == 0 && !pointer.alt &&
                view.Contains(mouse) && candidate >= 0)
            {
                if (mode == BoxGizmoMode.Yaw && !ReadYaw(view, camera, box.Center, mouse, out dragAngle))
                    return;

                GUIUtility.hotControl = control;
                editing = action;
                frameOrigin = previewOrigin;
                initial = box;
                dragMode = mode;
                origin = mouse;
                activeAxis = candidate % 3;
                changed = false;
                if (mode != BoxGizmoMode.Yaw)
                {
                    direction = Axis(box, candidate);
                    Vector3 anchor = Anchor(box, candidate, mode);
                    screenDirection = Point(view, camera, anchor + direction) - Point(view, camera, anchor);
                }
                pointer.Use();
            }
            else if (phase == EventType.MouseDrag && GUIUtility.hotControl == control && editing == action)
            {
                Drag(view, camera, mouse);
                pointer.Use();
                GUI.changed = true;
            }
            else if (phase == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                if (changed && editing != null)
                    AssetDatabase.SaveAssetIfDirty(editing);
                editing = null;
                pointer.Use();
            }
        }

        private void Drag(Rect view, Camera camera, Vector2 mouse)
        {
            if (dragMode == BoxGizmoMode.Yaw)
            {
                if (ReadYaw(view, camera, initial.Center, mouse, out float angle))
                    Apply(initial.Center, initial.Size, initial.Yaw + Mathf.DeltaAngle(dragAngle, angle));
                return;
            }

            if (screenDirection.sqrMagnitude < 4f)
                return;
            float meters = Vector2.Dot(mouse - origin, screenDirection) / screenDirection.sqrMagnitude;
            if (dragMode == BoxGizmoMode.Move)
            {
                Apply(initial.Center + direction * meters, initial.Size, initial.Yaw);
                return;
            }

            Vector3 size = initial.Size;
            size[activeAxis] = Mathf.Max(.02f, size[activeAxis] + meters);
            Vector3 center = initial.Center + direction * ((size[activeAxis] - initial.Size[activeAxis]) * .5f);
            Apply(center, size, initial.Yaw);
        }

        private void Apply(Vector3 center, Vector3 size, float yaw)
        {
            if (!changed)
            {
                Undo.RegisterCompleteObjectUndo(editing, "编辑攻击碰撞盒");
                changed = true;
            }
            editing.CustomHitBox = true;
            editing.HitBoxCenter = center - frameOrigin;
            editing.HitBoxSize = size;
            editing.HitBoxYaw = Mathf.DeltaAngle(0f, yaw);
            EditorUtility.SetDirty(editing);
        }

        private static int DrawAxes(Rect view, Camera camera, AttackBox box, BoxGizmoMode mode, Vector2 mouse, float nearest)
        {
            int candidate = -1;
            bool draw = Event.current.type == EventType.Repaint;
            if (draw) Handles.BeginGUI();
            Color saved = Handles.color;
            int count = mode == BoxGizmoMode.Size ? 6 : 3;
            for (int axis = 0; axis < count; axis++)
            {
                Vector2 center = Point(view, camera, box.Center);
                Vector2 tip = Point(view, camera, Anchor(box, axis, mode));
                if (!view.Contains(tip) || (tip - center).sqrMagnitude < 16f)
                    continue;
                float distance = Vector2.Distance(tip, mouse);
                bool highlighted = distance < 11f;
                if (distance < nearest)
                {
                    nearest = distance;
                    candidate = axis;
                }
                Rect marker = new(tip.x - 4f, tip.y - 4f, 8f, 8f);
                EditorGUIUtility.AddCursorRect(new Rect(tip.x - 9f, tip.y - 9f, 18f, 18f), MouseCursor.MoveArrow);
                if (draw)
                {
                    Handles.color = highlighted ? Color.white : Colors[axis % 3];
                    if (mode == BoxGizmoMode.Move)
                    {
                        Handles.DrawAAPolyLine(2.2f, center, tip);
                        Vector2 backward = (center - tip).normalized * 8f;
                        Vector2 wing = Vector2.Perpendicular(backward) * .55f;
                        Handles.DrawAAPolyLine(2.2f, tip + backward + wing, tip, tip + backward - wing);
                    }
                    else
                        EditorGUI.DrawRect(marker, Handles.color);
                }
            }
            if (draw)
            {
                Handles.color = saved;
                Handles.EndGUI();
            }
            return candidate;
        }

        private static float DrawRing(Rect view, Camera camera, AttackBox box, Vector2 mouse)
        {
            const int segments = 40;
            float radius = Mathf.Max(.35f, new Vector2(box.Size.x, box.Size.z).magnitude * .55f);
            Vector3[] ring = new Vector3[segments + 1];
            float nearest = float.PositiveInfinity;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i % segments) * (2f * Mathf.PI / segments);
                ring[i] = Point(view, camera, box.Center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius);
                if (i > 0)
                    nearest = Mathf.Min(nearest, HandleUtility.DistancePointLine(mouse, ring[i - 1], ring[i]));
            }

            Vector2 forward = Point(view, camera, box.Center + Quaternion.Euler(0f, box.Yaw, 0f) * Vector3.forward * radius);
            if (nearest < 11f && view.Contains(mouse))
                EditorGUIUtility.AddCursorRect(new Rect(mouse.x - 6f, mouse.y - 6f, 12f, 12f), MouseCursor.RotateArrow);
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                Color saved = Handles.color;
                Handles.color = nearest < 11f ? Color.white : Colors[1];
                Handles.DrawAAPolyLine(2.6f, ring);
                EditorGUI.DrawRect(new Rect(forward.x - 4f, forward.y - 4f, 8f, 8f), Handles.color);
                Handles.color = saved;
                Handles.EndGUI();
            }
            return nearest;
        }

        private static Vector3 Axis(AttackBox box, int handle) =>
            Quaternion.Euler(0f, box.Yaw, 0f) * Axes[handle % 3] * (handle >= 3 ? -1f : 1f);

        private static Vector3 Anchor(AttackBox box, int handle, BoxGizmoMode mode) =>
            box.Center + Axis(box, handle) * (mode == BoxGizmoMode.Size ? box.Size[handle % 3] * .5f : .52f);

        private static Vector2 Point(Rect view, Camera camera, Vector3 position)
        {
            Vector3 projected = camera.WorldToViewportPoint(position);
            return new Vector2(view.x + projected.x * view.width, view.y + (1f - projected.y) * view.height);
        }

        private static bool ReadYaw(Rect view, Camera camera, Vector3 center, Vector2 mouse, out float angle)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3((mouse.x - view.x) / view.width, 1f - (mouse.y - view.y) / view.height));
            var ground = new Plane(Vector3.up, center);
            if (ground.Raycast(ray, out float along))
            {
                Vector3 direction = ray.GetPoint(along) - center;
                angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                return direction.sqrMagnitude > .001f;
            }
            angle = 0f;
            return false;
        }
    }
}
