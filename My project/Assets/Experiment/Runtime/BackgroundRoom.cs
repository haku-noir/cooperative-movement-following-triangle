using UnityEngine;
using UnityEngine.Rendering;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 背景の静止した部屋（仕様書 §4.4）。
    ///
    /// 背景は装飾ではなく必須の実験要素である。被験者 B が自身の頭部運動を知覚するための
    /// 視覚的な「地」であり、無地空間にしてはならない。一方で、余計な視覚要素を足さないという
    /// 方針から、床のグリッドと壁の規則模様だけを手続き生成し、装飾は一切置かない。
    ///
    /// * 部屋は試行中に一切動かない（この GameObject は誰からも駆動されない）
    /// * シェーダは Unlit。照明計算をなくすことで陰影が奥行き手がかりとして混入するのを防ぐ
    /// * 影は無効（§4.4）
    /// </summary>
    [DisallowMultipleComponent]
    public class BackgroundRoom : MonoBehaviour
    {
        [Header("部屋の寸法 [m]")]
        [SerializeField] private float width = 6f;
        [SerializeField] private float depth = 6f;
        [SerializeField] private float height = 3f;

        [Header("模様")]
        [SerializeField]
        [Tooltip("床グリッドの 1 マスの辺長 [m]。")]
        private float floorGridCellSize = 0.5f;

        [SerializeField]
        [Tooltip("壁模様の 1 マスの辺長 [m]。")]
        private float wallPatternCellSize = 0.5f;

        [SerializeField] private Color surfaceColor = new Color(0.32f, 0.33f, 0.35f, 1f);
        [SerializeField] private Color lineColor = new Color(0.60f, 0.62f, 0.66f, 1f);

        private const string ShaderName = "Universal Render Pipeline/Unlit";
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake() => Rebuild();

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            var shader = Shader.Find(ShaderName) ?? Shader.Find("Unlit/Texture");

            var floorMaterial = CreateMaterial(shader, BuildGridTexture(512, 6), "RoomFloor");
            var wallMaterial = CreateMaterial(shader, BuildCheckerTexture(512, 8), "RoomWall");

            float hw = width * 0.5f;
            float hd = depth * 0.5f;

            // 床：内側（部屋の中）を向くように配置する。
            AddQuad("Floor", floorMaterial,
                new Vector3(0f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f),
                new Vector2(width, depth), width / floorGridCellSize, depth / floorGridCellSize);

            AddQuad("Ceiling", wallMaterial,
                new Vector3(0f, height, 0f), Quaternion.Euler(-90f, 0f, 0f),
                new Vector2(width, depth), width / wallPatternCellSize, depth / wallPatternCellSize);

            AddQuad("Wall_North", wallMaterial,
                new Vector3(0f, height * 0.5f, hd), Quaternion.Euler(0f, 180f, 0f),
                new Vector2(width, height), width / wallPatternCellSize, height / wallPatternCellSize);

            AddQuad("Wall_South", wallMaterial,
                new Vector3(0f, height * 0.5f, -hd), Quaternion.identity,
                new Vector2(width, height), width / wallPatternCellSize, height / wallPatternCellSize);

            AddQuad("Wall_East", wallMaterial,
                new Vector3(hw, height * 0.5f, 0f), Quaternion.Euler(0f, -90f, 0f),
                new Vector2(depth, height), depth / wallPatternCellSize, height / wallPatternCellSize);

            AddQuad("Wall_West", wallMaterial,
                new Vector3(-hw, height * 0.5f, 0f), Quaternion.Euler(0f, 90f, 0f),
                new Vector2(depth, height), depth / wallPatternCellSize, height / wallPatternCellSize);
        }

        private void AddQuad(
            string quadName, Material material, Vector3 localPosition, Quaternion localRotation,
            Vector2 size, float tileU, float tileV)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = quadName;
            quad.transform.SetParent(transform, worldPositionStays: false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = localRotation;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            // タイリングは面ごとに変わるため、面ごとの MaterialPropertyBlock で指定する。
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var block = new MaterialPropertyBlock();
            var scaleOffset = new Vector4(Mathf.Max(1f, tileU), Mathf.Max(1f, tileV), 0f, 0f);
            block.SetVector("_BaseMap_ST", scaleOffset);
            block.SetVector("_MainTex_ST", scaleOffset);
            renderer.SetPropertyBlock(block);
        }

        private Material CreateMaterial(Shader shader, Texture2D texture, string materialName)
        {
            var material = new Material(shader) { name = materialName };
            if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, texture);
            if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, texture);
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, Color.white);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, Color.white);
            return material;
        }

        /// <summary>1 マスぶんのグリッド線テクスチャ。床用。</summary>
        private Texture2D BuildGridTexture(int resolution, int lineWidthPixels)
        {
            var pixels = new Color32[resolution * resolution];
            Color32 fill = surfaceColor;
            Color32 line = lineColor;

            for (int y = 0; y < resolution; y++)
            {
                bool onHorizontalLine = y < lineWidthPixels || y >= resolution - lineWidthPixels;
                for (int x = 0; x < resolution; x++)
                {
                    bool onVerticalLine = x < lineWidthPixels || x >= resolution - lineWidthPixels;
                    pixels[y * resolution + x] = (onHorizontalLine || onVerticalLine) ? line : fill;
                }
            }

            return BuildTexture(resolution, pixels, "RoomGrid");
        }

        /// <summary>規則的な市松模様。壁・天井用。</summary>
        private Texture2D BuildCheckerTexture(int resolution, int cellsPerSide)
        {
            var pixels = new Color32[resolution * resolution];
            int cell = Mathf.Max(1, resolution / cellsPerSide);
            Color32 a = surfaceColor;
            Color32 b = Color.Lerp(surfaceColor, lineColor, 0.45f);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    bool even = ((x / cell) + (y / cell)) % 2 == 0;
                    pixels[y * resolution + x] = even ? a : b;
                }
            }

            return BuildTexture(resolution, pixels, "RoomChecker");
        }

        private static Texture2D BuildTexture(int resolution, Color32[] pixels, string textureName)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: true)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Repeat,

                // 遠方でのモアレを抑える。模様は「地」であって刺激ではないため、
                // ちらつきが課題への注意を奪わないことを優先する。
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true);
            return texture;
        }
    }
}
