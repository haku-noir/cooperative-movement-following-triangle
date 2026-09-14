using TMPro;
using UnityEngine;

namespace FollowingTriangle.Runtime
{
    /// <summary>
    /// 記録モード中に演者 A へ出すガイド表示。
    ///
    /// これは記録（刺激作成）専用であり、実験モードの刺激提示には一切使わない。
    /// 実験モードで被験者 B に余計な視覚要素を足さないという方針は、
    /// この HUD がそちらのシーンに存在しないことで担保する。
    ///
    /// 頭部に追従させる。演者は区間2 で大きく並進するため、ワールド固定にすると
    /// 視野から外れてガイドとして機能しなくなる。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecorderHud : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("追従対象の頭部 Transform。実機では CenterEyeAnchor。")]
        private Transform headTransform;

        [SerializeField] private float distanceMeters = 1.5f;
        [SerializeField] private float verticalOffsetMeters = -0.35f;
        [SerializeField] private float characterSize = 0.06f;
        [SerializeField] private Color normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.55f, 0.2f, 1f);

        private TextMeshPro mainText;
        private TextMeshPro warningText;
        private bool built;

        public Transform HeadTransform
        {
            get => headTransform;
            set => headTransform = value;
        }

        private void Awake() => EnsureBuilt();

        private void LateUpdate()
        {
            if (headTransform == null) return;

            EnsureBuilt();

            // ヨーのみ追従させる。ピッチまで追うと、うなずき動作のたびに
            // 文字が視野内で上下に流れて読みにくい。
            Vector3 forward = headTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            transform.position = headTransform.position
                                 + forward * distanceMeters
                                 + Vector3.up * verticalOffsetMeters;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void SetMessage(string message)
        {
            EnsureBuilt();
            mainText.text = message ?? "";
        }

        public void SetWarning(string message)
        {
            EnsureBuilt();
            bool hasWarning = !string.IsNullOrEmpty(message);
            warningText.text = message ?? "";
            warningText.gameObject.SetActive(hasWarning);
        }

        public void SetVisible(bool visible)
        {
            EnsureBuilt();
            mainText.gameObject.SetActive(visible);
            if (!visible) warningText.gameObject.SetActive(false);
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            mainText = CreateText("Main", Vector3.zero, normalColor);
            warningText = CreateText("Warning", new Vector3(0f, -0.22f, 0f), warningColor);
            warningText.gameObject.SetActive(false);
        }

        private TextMeshPro CreateText(string textName, Vector3 localPosition, Color color)
        {
            var host = new GameObject(textName);
            host.transform.SetParent(transform, worldPositionStays: false);
            host.transform.localPosition = localPosition;

            var text = host.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = characterSize * 100f;
            text.color = color;

            // enableWordWrapping は TMP で非推奨。textWrappingMode が後継。
            text.textWrappingMode = TextWrappingModes.Normal;
            text.rectTransform.sizeDelta = new Vector2(1.6f, 0.4f);

            if (text.font == null)
            {
                Debug.LogError(
                    $"[{nameof(RecorderHud)}] TextMeshPro のフォントが解決できません。" +
                    "メニュー Following Triangle > Import TMP Essential Resources を実行してください。",
                    this);
            }

            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return text;
        }
    }
}
