using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DemonsWithin.Editor
{
    internal static class TrainingArenaBuilder
    {
        private const string ScenePath = "Assets/DemonsWithin/Scenes/TrainingArena.unity";
        private const string DuelPath = "Assets/DemonsWithin/Scenes/PhaseOneArena.unity";

        [MenuItem("Tools/Demons Within/打开连招训练场", priority = 104)]
        private static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return;

            var scene = EditorSceneManager.OpenScene(DuelPath);
            var arena = Object.FindAnyObjectByType<DuelArena>();
            var data = new SerializedObject(arena);
            data.FindProperty("comboTraining").boolValue = true;
            var dummy = (DuelFighter)data.FindProperty("demon").objectReferenceValue;
            var playerSpawn = (Transform)data.FindProperty("playerSpawn").objectReferenceValue;
            var dummySpawn = (Transform)data.FindProperty("demonSpawn").objectReferenceValue;
            data.ApplyModifiedPropertiesWithoutUndo();

            Vector3 center = (playerSpawn.position + dummySpawn.position) * 0.5f;
            Vector3 direction = Vector3.ProjectOnPlane(dummySpawn.position - playerSpawn.position, Vector3.up).normalized;
            playerSpawn.position = center - direction * .65f;
            dummySpawn.position = center + direction * .65f;
            playerSpawn.rotation = Quaternion.LookRotation(direction);
            dummySpawn.rotation = Quaternion.LookRotation(-direction);
            dummy.gameObject.name = "Training Robot";
            dummy.GetComponent<FighterPresentation>().enabled = false;
            foreach (Transform child in dummy.transform)
                child.gameObject.SetActive(false);

            MakeRobot(dummy.transform);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void MakeRobot(Transform parent)
        {
            var robot = new GameObject("Robot model").transform;
            robot.SetParent(parent, false);
            Material shell = Material("Player Trim");
            Material frame = Material("Stone Boundary");
            Material lit = Material("Player Accent");

            Part(robot, PrimitiveType.Cylinder, "Pedestal", new Vector3(0f, .10f, 0f), new Vector3(.70f, .10f, .70f), frame);
            Part(robot, PrimitiveType.Cylinder, "Column", new Vector3(0f, .60f, 0f), new Vector3(.24f, .46f, .24f), shell);
            Part(robot, PrimitiveType.Cube, "Chest", new Vector3(0f, 1.10f, 0f), new Vector3(.51f, .61f, .33f), shell);
            Part(robot, PrimitiveType.Cube, "Target plate", new Vector3(0f, 1.10f, .18f), new Vector3(.33f, .29f, .035f), frame);
            Part(robot, PrimitiveType.Sphere, "Hit marker", new Vector3(0f, 1.10f, .205f), new Vector3(.10f, .10f, .045f), lit);
            Part(robot, PrimitiveType.Cube, "Head", new Vector3(0f, 1.62f, 0f), new Vector3(.39f, .26f, .30f), shell);
            Part(robot, PrimitiveType.Cube, "Visor", new Vector3(0f, 1.64f, .16f), new Vector3(.30f, .085f, .035f), lit);
            foreach (int side in new[] { -1, 1 })
            {
                Part(robot, PrimitiveType.Sphere, "Shoulder", new Vector3(side * .30f, 1.29f, 0f), Vector3.one * .22f, frame);
                Part(robot, PrimitiveType.Cube, "Arm", new Vector3(side * .35f, 1.05f, 0f), new Vector3(.15f, .36f, .18f), shell);
            }
        }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"Assets/DemonsWithin/Art/{name}.mat");

        private static void Part(Transform parent, PrimitiveType shape, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }
    }
}
