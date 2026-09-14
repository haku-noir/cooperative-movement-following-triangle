using FollowingTriangle.Core;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 身体三角形を 1 つ描画する。自己三角形にも相手三角形にも、同じクラスの同じ設定で使う。
    ///
    /// 統制上の要点：
    ///   * このクラスは <see cref="Condition"/> を知らない。受け取るのは
    ///     <see cref="TriangleVisualConfig"/> だけであり、条件ごとの分岐は
    ///     <see cref="ConditionVisuals"/> 側に一箇所だけ存在する。
    ///     したがって C1 と C2 は、このクラスから見て完全に同一の入力になる。
    ///   * 「自己か相手か」を表すフィールドも持たない。自他で見た目を変える経路が存在しない
    ///     （仕様書 §4.2, §4.3）。
    ///
    /// 仕様書 §10-2（自己と相手が重なった際の描画順序・深度テストの扱い）は未確定のため、
    /// 現状は不透明・通常の深度テストという Unity 既定のままにしてある。
    /// </summary>
    [DisallowMultipleComponent]
    public class TriangleView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("頂点球と辺に使うシェーダ。既定は URP/Unlit。" +
                 "Unlit にしているのは、照明由来の陰影が奥行き手がかりとして混入するのを避けるため。")]
        private Shader shader;

        private Transform[] vertexSpheres;
        private LineRenderer edgeRenderer;
        private Material vertexMaterial;
        private Material edgeMaterial;
        private TriangleVisualConfig config;
        private bool built;

        private const string DefaultShaderName = "Universal Render Pipeline/Unlit";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>描画パラメータを適用する。条件が変わるたびに呼ぶ。</summary>
        public void Configure(TriangleVisualConfig visualConfig)
        {
            config = visualConfig;
            EnsureBuilt();
            ApplyConfig();
        }

        /// <summary>3 頂点のワールド座標を与える。毎フレーム呼ぶ。</summary>
        public void SetVertices(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            EnsureBuilt();

            vertexSpheres[0].position = v0;
            vertexSpheres[1].position = v1;
            vertexSpheres[2].position = v2;

            if (config.drawEdges)
            {
                edgeRenderer.SetPosition(0, v0);
                edgeRenderer.SetPosition(1, v1);
                edgeRenderer.SetPosition(2, v2);
            }
        }

        /// <summary>三角形全体の表示・非表示。</summary>
        public void SetVisible(bool visible)
        {
            EnsureBuilt();

            foreach (var sphere in vertexSpheres)
            {
                sphere.gameObject.SetActive(visible);
            }

            edgeRenderer.enabled = visible && config.drawEdges;
        }

        private void Awake() => EnsureBuilt();

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            var resolvedShader = shader != null ? shader : Shader.Find(DefaultShaderName);
            if (resolvedShader == null)
            {
                // URP が見つからない構成でも描画自体は続けられるようにする。
                resolvedShader = Shader.Find("Unlit/Color");
            }

            vertexMaterial = new Material(resolvedShader) { name = "TriangleVertex (runtime)" };
            edgeMaterial = new Material(resolvedShader) { name = "TriangleEdge (runtime)" };

            vertexSpheres = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"V{i}";
                sphere.transform.SetParent(transform, worldPositionStays: false);

                // コライダは不要。物理演算が刺激に干渉する余地を残さない。
                var collider = sphere.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                var renderer = sphere.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = vertexMaterial;
                DisableShadows(renderer);

                vertexSpheres[i] = sphere.transform;
            }

            var edgeObject = new GameObject("Edges");
            edgeObject.transform.SetParent(transform, worldPositionStays: false);
            edgeRenderer = edgeObject.AddComponent<LineRenderer>();
            edgeRenderer.useWorldSpace = true;
            edgeRenderer.positionCount = 3;
            edgeRenderer.loop = true;
            edgeRenderer.numCapVertices = 0;
            edgeRenderer.numCornerVertices = 0;
            edgeRenderer.sharedMaterial = edgeMaterial;
            DisableShadows(edgeRenderer);
        }

        private void ApplyConfig()
        {
            float diameter = config.vertexSphereDiameter;
            foreach (var sphere in vertexSpheres)
            {
                // Unity の球プリミティブは scale 1 で直径 1 m。
                sphere.localScale = new Vector3(diameter, diameter, diameter);
            }

            SetColor(vertexMaterial, config.vertexColor);
            SetColor(edgeMaterial, config.edgeColor);

            edgeRenderer.widthMultiplier = config.edgeLineWidth;
            edgeRenderer.enabled = config.drawEdges;
        }

        private static void SetColor(Material material, Color color)
        {
            // URP は _BaseColor、ビルトインの Unlit/Color は _Color を使う。
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        }

        private static void DisableShadows(Renderer renderer)
        {
            // 仕様書 §4.4：影による追加手がかりを避ける。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void OnDestroy()
        {
            if (vertexMaterial != null) Destroy(vertexMaterial);
            if (edgeMaterial != null) Destroy(edgeMaterial);
        }
    }
}
