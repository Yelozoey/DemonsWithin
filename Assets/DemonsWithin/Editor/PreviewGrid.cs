using UnityEngine;

namespace DemonsWithin.Editor
{
    internal sealed class PreviewGrid
    {
        private readonly Mesh mesh;
        private readonly Texture2D texture;
        private readonly Material material;

        public GameObject Floor { get; }

        public PreviewGrid()
        {
            const float extent = 8f;
            Floor = new GameObject("Preview ground grid", typeof(MeshFilter), typeof(MeshRenderer));
            Floor.hideFlags = HideFlags.HideAndDontSave;
            mesh = new Mesh { name = "Preview ground", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-extent, -.003f, -extent), new Vector3(extent, -.003f, -extent),
                new Vector3(-extent, -.003f, extent), new Vector3(extent, -.003f, extent)
            };
            mesh.uv = new[]
            {
                new Vector2(-extent, -extent), new Vector2(extent, -extent),
                new Vector2(-extent, extent), new Vector2(extent, extent)
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            Floor.GetComponent<MeshFilter>().sharedMesh = mesh;

            const int pixels = 64;
            texture = new Texture2D(pixels, pixels, TextureFormat.RGBA32, true)
            {
                name = "0.5 m preview grid", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32 dark = new(40, 49, 60, 255);
            Color32 light = new(55, 65, 78, 255);
            Color32 meter = new(83, 97, 114, 255);
            var pattern = new Color32[pixels * pixels];
            for (int y = 0; y < pixels; y++)
                for (int x = 0; x < pixels; x++)
                    pattern[y * pixels + x] = x <= 1 || y <= 1 ? meter : (x < pixels / 2) == (y < pixels / 2) ? dark : light;
            texture.SetPixels32(pattern);
            texture.Apply();

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            material = new Material(shader) { name = "Preview grid", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex", texture);
            Floor.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(mesh);
        }
    }
}
