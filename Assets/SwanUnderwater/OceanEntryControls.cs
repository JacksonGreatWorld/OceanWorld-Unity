using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace SwanUnderwater
{
    /// <summary>One optional seated-height presentation action. The ordinary cinematic still
    /// finishes without hands/controllers, and this UI never releases world loading gates.</summary>
    [DefaultExecutionOrder(98)]
    public sealed class OceanEntryControls : MonoBehaviour
    {
        sealed class Pointer
        {
            public readonly OceanMenuPressGate gate = new OceanMenuPressGate();
            public LineRenderer line;
        }
        readonly Pointer[] pointers = { new Pointer(), new Pointer(), new Pointer(), new Pointer() };
        OceanEntry entry;
        Camera view;
        HandSwimController hands;
        GameObject root;
        RectTransform buttonRect;
        Button button;
        Text label, status;
        Material material;
        int activatedFrame = -1;

        public void Initialize(OceanEntry owner, Camera camera, Font font)
        {
            entry = owner; view = camera; hands = FindFirstObjectByType<HandSwimController>();
            Shader shader = Resources.Load<Shader>("PerformanceHudUI");
            if (shader == null || font == null) return; // Presentation itself remains fully usable.
            material = new Material(shader) { name = "Entry optional controls", renderQueue = 4190 };
            root = new GameObject("Optional entry skip", typeof(RectTransform), typeof(Canvas));
            root.layer = OceanEntry.IntroLayer;
            root.transform.SetParent(view.transform, false);
            root.transform.localPosition = new Vector3(0f, -.34f, 1.3f);
            root.transform.localScale = Vector3.one * .001f;
            ((RectTransform)root.transform).sizeDelta = new Vector2(600, 130);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view;
            var host = new GameObject("Skip cinematic button", typeof(RectTransform), typeof(Image), typeof(Button));
            host.layer = OceanEntry.IntroLayer; host.transform.SetParent(root.transform, false);
            buttonRect = (RectTransform)host.transform; buttonRect.sizeDelta = new Vector2(400, 60);
            var image = host.GetComponent<Image>(); image.material = material; image.color = new Color(.025f, .12f, .18f, .95f);
            button = host.GetComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Activate);
            label = Label(host.transform, font, "跳过入场", 27, Vector2.zero, new Vector2(390, 56));
            status = Label(root.transform, font, "指向后松手，再捏合 · 也可用控制器", 19, new Vector2(0, -54), new Vector2(590, 42));
            foreach (Pointer pointer in pointers)
            {
                var ray = new GameObject("Entry pointer"); ray.layer = OceanEntry.IntroLayer; ray.transform.SetParent(transform, false);
                pointer.line = ray.AddComponent<LineRenderer>(); pointer.line.sharedMaterial = material;
                pointer.line.positionCount = 2; pointer.line.useWorldSpace = true;
                pointer.line.startWidth = .0018f; pointer.line.endWidth = .001f;
                pointer.line.startColor = pointer.line.endColor = new Color(.8f, .95f, 1f, .85f);
                pointer.line.shadowCastingMode = ShadowCastingMode.Off; pointer.line.receiveShadows = false;
                pointer.line.enabled = false;
            }
        }

        Text Label(Transform parent, Font font, string text, int size, Vector2 position, Vector2 dimensions)
        {
            var item = new GameObject("Entry control text", typeof(RectTransform), typeof(Text));
            item.layer = OceanEntry.IntroLayer; item.transform.SetParent(parent, false);
            var value = item.GetComponent<Text>(); value.font = font; value.fontSize = size; value.material = material;
            value.text = text; value.alignment = TextAnchor.MiddleCenter; value.raycastTarget = false;
            value.horizontalOverflow = HorizontalWrapMode.Wrap; value.verticalOverflow = VerticalWrapMode.Truncate;
            value.color = new Color(.9f, .97f, 1f, 1f); value.rectTransform.anchoredPosition = position; value.rectTransform.sizeDelta = dimensions;
            return value;
        }

        void LateUpdate()
        {
            bool visible = entry != null && entry.CanOperatePlayback && OceanEntry.BlocksGameplay;
            if (root == null) return;
            root.SetActive(visible);
            if (!visible || hands == null || !hands.CanReadTrackedVisuals) { ResetPointers(); return; }
            button.interactable = !entry.SkipRequested;
            label.text = entry.SkipRequested ? "已请求跳过" : "跳过入场";
            status.text = entry.IsReplay ? "入场重看 · 进度保留 · 结束回到菜单" :
                entry.SkipRequested ? "加载完成后安全进入 · 正在准备" : "指向后松手，再捏合 · 也可用控制器";
            for (int i = 0; i < 2; i++)
            {
                bool fresh = hands.TryGetEntryHandPointer(i == 0 ? OceanHandSide.HandLeft : OceanHandSide.HandRight,
                    out Ray ray, out float strength, out bool pinchReady);
                Process(pointers[i], ray, strength, fresh && pinchReady);
            }
            for (int i = 0; i < 2; i++)
            {
                InputDevice device = InputDevices.GetDeviceAtXRNode(i == 0 ? XRNode.LeftHand : XRNode.RightHand);
                Ray ray = default; float strength = 0f;
                bool fresh = hands.TrackingSpace != null && device.isValid &&
                    device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                    device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) &&
                    device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
                if (fresh)
                {
                    // Read again only after the complete validity chain; no stale controller pose.
                    device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                    device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
                    ray = new Ray(hands.TrackingSpace.TransformPoint(position), hands.TrackingSpace.TransformDirection(rotation * Vector3.forward));
                    device.TryGetFeatureValue(CommonUsages.trigger, out strength);
                    if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed) strength = 1f;
                }
                Process(pointers[i + 2], ray, strength, fresh);
            }
        }

        void Process(Pointer pointer, Ray ray, float strength, bool fresh)
        {
            float distance = 0f;
            bool hit = fresh && Hit(ray, out distance);
            pointer.line.enabled = hit && button.interactable;
            if (pointer.line.enabled) { pointer.line.SetPosition(0, ray.origin); pointer.line.SetPosition(1, ray.GetPoint(distance)); }
            if (pointer.gate.Step(Time.frameCount, Time.unscaledDeltaTime, fresh, strength, hit ? 0 : -1, button.interactable) == 0)
                button.onClick.Invoke();
        }
        bool Hit(Ray ray, out float distance)
        {
            distance = 0;
            if (!Finite(ray.origin) || !Finite(ray.direction) || ray.direction.sqrMagnitude < .5f) return false;
            float denominator = Vector3.Dot(ray.direction, buttonRect.forward);
            if (denominator <= .0001f) return false;
            distance = Vector3.Dot(buttonRect.position - ray.origin, buttonRect.forward) / denominator;
            if (distance < .02f || distance > 3f) return false;
            Vector3 local = buttonRect.InverseTransformPoint(ray.GetPoint(distance));
            return buttonRect.rect.Contains(new Vector2(local.x, local.y));
        }
        static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        void Activate()
        {
            if (entry == null || !entry.CanOperatePlayback || !OceanEntry.BlocksGameplay || activatedFrame == Time.frameCount) return;
            activatedFrame = Time.frameCount; entry.RequestSkip(); ResetPointers();
        }
        void ResetPointers()
        { foreach (Pointer pointer in pointers) { pointer.gate.Reset(); if (pointer.line != null) pointer.line.enabled = false; } }
        void OnDisable() { if (root != null) root.SetActive(false); ResetPointers(); }
        void OnDestroy()
        {
            if (root != null) { if (Application.isPlaying) Destroy(root); else DestroyImmediate(root); }
            if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        }
#if UNITY_EDITOR
        public RectTransform ButtonForValidation => buttonRect;
        public void ClickForValidation() => button?.onClick.Invoke();
#endif
    }
}
