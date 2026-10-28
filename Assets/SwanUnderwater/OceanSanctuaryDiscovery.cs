using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Optional, quiet map discoveries on existing swim routes. Never moves or blocks the player.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class OceanSanctuaryDiscovery : MonoBehaviour
    {
        public readonly struct Site
        {
            public readonly string name, hint;
            public readonly Vector2 offset;
            public readonly float radius;
            public Site(string name, string hint, float x, float z, float radius)
            { this.name = name; this.hint = hint; offset = new Vector2(x, z); this.radius = radius; }
        }

        public static IReadOnlyList<Site> Sites { get; } = Array.AsReadOnly(new[] {
            new Site("晨光礁拱", "从拱洞望向深蓝，留意穿行的鱼群", 0, 31, 10),
            new Site("层礁回廊", "沿层叠礁壁慢游，观察礁缝里的小鱼", -65, 20, 17),
            new Site("珊瑚庭院", "停下来看看，珊瑚之间也有生命在活动", -45, -48, 16),
            new Site("摇曳草海", "顺着海草的摆动，寻找沙地与草丛中的贝壳", -12, -64, 17),
            new Site("静水寻贝湾", "低头探索沙地，靠近的贝壳会微微发光", 52, -55, 16),
            new Site("海扇峡谷", "绕过礁角，观察海扇与鱼群的缓慢摆动", 79, 3, 16),
            new Site("蓝渊裂谷", "抬头望向光，感受远处海域的尺度", -15, 89, 16)
        });

        readonly RaycastHit[] occlusionHits = new RaycastHit[12];
        OceanSiteVisitProgress progress;
        UnderwaterWorld world;
        Camera view;
        TreasureHunt hunt;
        TreasureFeedback feedback;
        Vector3[] landmarks;
        float sampleElapsed;
        bool paused, focused = true;
        public int DiscoveredCount => progress?.VisitedCount ?? 0;

        public static OceanSanctuaryDiscovery Build(Transform parent, UnderwaterWorld owner, Camera camera)
        {
            if (parent == null || owner == null || camera == null) throw new ArgumentNullException("Discovery dependencies");
            var existing = parent.GetComponentInChildren<OceanSanctuaryDiscovery>(true);
            if (existing != null) return existing;
            var root = new GameObject("Sanctuary · Seven sea routes");
            root.transform.SetParent(parent, false);
            var discovery = root.AddComponent<OceanSanctuaryDiscovery>();
            discovery.world = owner; discovery.view = camera;
            discovery.progress = new OceanSiteVisitProgress(Sites.Count);
            discovery.landmarks = new Vector3[Sites.Count];
            for (int i = 0; i < Sites.Count; i++)
            {
                Vector2 at = Sites[i].offset;
                Vector3 point = owner.ActiveSpawn + new Vector3(at.x, 0, at.y);
                point.y = UnderwaterWorld.SampleRenderedSeabedHeight(point.x, point.z) + 1.6f;
                discovery.landmarks[i] = point;
            }
            return discovery;
        }

        void LateUpdate()
        {
            if (world == null || !world.IsReady || view == null || !view.isActiveAndEnabled || paused || !focused ||
                world.ActiveEnvironment != OceanEnvironmentKind.CoralSanctuary || OceanEntry.IsLoading)
            { Interrupt(); return; }
            // Campaign presentation is constructed after the first environment. Resolve once
            // after readiness, rather than searching the hierarchy at input/frame frequency.
            if (hunt == null)
            {
                hunt = world.GetComponentInChildren<TreasureHunt>(true);
                if (hunt != null) feedback = hunt.GetComponent<TreasureFeedback>();
            }
            if (hunt == null || feedback == null || !hunt.GameplayActive || !feedback.CanShowExplorationMessage)
            { Interrupt(); return; }
            sampleElapsed += Time.unscaledDeltaTime;
            if (sampleElapsed < .2f) return;
            float dt = Mathf.Min(sampleElapsed, .25f); sampleElapsed = 0;
            Vector3 eye = view.transform.position;
            int candidate = -1; float nearest = float.PositiveInfinity;
            for (int i = 0; i < Sites.Count; i++)
            {
                if (progress.HasVisited(i)) continue;
                Vector3 delta = eye - landmarks[i];
                // Looking from far above a habitat is not the same as exploring it.
                if (delta.y < -1.8f || delta.y > 8f) continue;
                float distance = delta.x * delta.x + delta.z * delta.z;
                if (distance > Sites[i].radius * Sites[i].radius || distance >= nearest) continue;
                if (!LandmarkVisible(eye, landmarks[i])) continue;
                nearest = distance; candidate = i;
            }
            int found = progress.Tick(candidate, dt, true);
            if (found < 0) return;
            Site site = Sites[found];
            string text = progress.VisitedCount == Sites.Count
                ? "七处秘境已探索 · 珊瑚海巡游完成\n继续寻找本关的鱼、贝壳和金币"
                : $"发现 · {site.name}（{progress.VisitedCount}/{Sites.Count}）\n{site.hint}";
            bool allVisited = progress.VisitedCount == Sites.Count;
            int visited = progress.VisitedCount, total = Sites.Count;
            string siteName = site.name, siteHint = site.hint;
            feedback.TryShowLocalizedExplorationMessage(text, () => allVisited ?
                OceanLanguage.Text("七处秘境已探索 · 珊瑚海巡游完成\n继续寻找本关的鱼、贝壳和金币") :
                OceanLanguage.Format("发现 · {0}（{1}/{2}）\n{3}", OceanLanguage.Text(siteName), visited, total, OceanLanguage.Text(siteHint)));
            Debug.Log($"[SanctuaryDiscovery] site={found} name={site.name} visited={progress.VisitedCount}/{Sites.Count}; session only, no score or stars");
        }

        bool LandmarkVisible(Vector3 eye, Vector3 target)
        {
            Vector3 direction = target - eye; float distance = direction.magnitude;
            if (distance < .4f) return true;
            int count = Physics.RaycastNonAlloc(eye, direction / distance, occlusionHits, distance - .15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count >= occlusionHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = occlusionHits[i].transform;
                if (hit != null && hit.IsChildOf(world.ActiveEnvironmentRoot)) return false;
            }
            return true;
        }

        void Interrupt() { sampleElapsed = 0; progress?.Interrupt(); }
        void OnDisable() => Interrupt();
        void OnApplicationPause(bool value) { paused = value; if (value) Interrupt(); }
        void OnApplicationFocus(bool value) { focused = value; if (!value) Interrupt(); }
    }
}
