using System;
using UnityEngine;

namespace DemonsWithin
{
    internal sealed class DuelLucyPanel : MonoBehaviour
    {
        private static readonly string[] Categories = CategoryNames();
        private static readonly string[] Roles = { "玩家", "心魔" };
        private DuelArena arena;
        private LucyCombatLibrary library;
        private LucyCombatEntry selected;
        private string search = "";
        private int category;
        private Vector2 scroll;
        private bool useDemon;
        private float previousTimeScale;

        public bool IsOpen { get; private set; }

        public void Bind(DuelArena owner, LucyCombatLibrary catalog)
        {
            arena = owner;
            library = catalog;
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            IsOpen = true;
        }

        private void OnDisable() => Close();

        private void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            if (Time.timeScale == 0f)
                Time.timeScale = previousTimeScale;
        }

        private void OnGUI()
        {
            if (!IsOpen)
                return;

            float width = Mathf.Min(750f, Screen.width - 24f);
            float height = Mathf.Min(610f, Screen.height - 24f);
            var bounds = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(bounds, GUIContent.none, GUI.skin.window);
            var content = new Rect(bounds.x + 14, bounds.y + 17, bounds.width - 28, bounds.height - 28);
            using (new GUILayout.AreaScope(content))
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Lucy   ·   {library.Entries.Count} 个战斗片段");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("关闭  F4", GUILayout.Width(85f)))
                        Close();
                }

                int columns = width < 520f ? 3 : 5;
                int newCategory = GUILayout.SelectionGrid(category, Categories, columns);
                GUI.SetNextControlName("LucyAnimationSearch");
                string newSearch = GUILayout.TextField(search);
                if (newCategory != category || newSearch != search)
                {
                    category = newCategory;
                    search = newSearch;
                    scroll = Vector2.zero;
                }

                using (var list = new GUILayout.ScrollViewScope(scroll))
                {
                    scroll = list.scrollPosition;
                    foreach (LucyCombatEntry entry in library.Entries)
                    {
                        if (category != 0 && (int)entry.Kind != category - 1 ||
                            entry.Source.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (GUILayout.Toggle(selected == entry, entry.Source, GUI.skin.button))
                            selected = entry;
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (!arena.IsComboTraining)
                        useDemon = GUILayout.SelectionGrid(useDemon ? 1 : 0, Roles, 2, GUILayout.Width(140)) == 1;
                    bool trainingPair = arena.IsComboTraining && selected?.Partner != null;
                    using (new GuiEnabledScope(selected?.Clip != null && !trainingPair))
                    {
                        if (GUILayout.Button(selected?.Partner != null ? "双人演示" : "场景演示"))
                        {
                            arena.DemonstrateLucy(selected, useDemon);
                            Close();
                        }
                    }
                    if (selected?.Action?.Attack != true)
                    {
                        using (new GuiEnabledScope(selected?.CanEquip == true && !trainingPair))
                        {
                            if (GUILayout.Button(selected?.Kind == LucyCombatKind.Execution ? "装备终结演出并重开" : "装备并重开"))
                            {
                                arena.EquipLucy(selected, useDemon);
                                Close();
                            }
                        }
                    }
                    if (GUILayout.Button("恢复基础动作"))
                    {
                        arena.ClearLucy();
                        Close();
                    }
                }

                GUILayout.Label(selected == null ? "选择片段。按名称搜索，例如 Kick、FightAvoid 或 Execution。" :
                    $"{LucyCombatEntry.Label(selected.Kind)}   ·   {selected.Take}   ·   {selected.Action.Length} 帧");
            }
        }

        private static string[] CategoryNames()
        {
            LucyCombatKind[] kinds = (LucyCombatKind[])Enum.GetValues(typeof(LucyCombatKind));
            var names = new string[kinds.Length + 1];
            names[0] = "全部";
            foreach (LucyCombatKind kind in kinds)
                names[(int)kind + 1] = LucyCombatEntry.Label(kind);
            return names;
        }

        private readonly struct GuiEnabledScope : IDisposable
        {
            private readonly bool previous;

            public GuiEnabledScope(bool enabled)
            {
                previous = GUI.enabled;
                GUI.enabled = previous && enabled;
            }

            public void Dispose() => GUI.enabled = previous;
        }
    }
}
