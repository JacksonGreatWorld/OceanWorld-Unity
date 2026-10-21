using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using HandType = SwanUnderwater.OceanHandSide;

namespace SwanUnderwater
{
    [DefaultExecutionOrder(115)]
    public sealed class OceanNaturalist : MonoBehaviour
    {
        sealed class Subject
        {
            public int species;
            public Transform model;
            public Renderer[] renderers;
            public Bounds Bounds()
            {
                bool first = true; var result = new Bounds(model.position, Vector3.one * .12f);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    if (first) { result = renderers[i].bounds; first = false; } else result.Encapsulate(renderers[i].bounds);
                }
                return result;
            }
        }
        UnderwaterWorld world;
        OceanCampaignController campaign;
        TreasureHunt hunt;
        HandSwimController hands;
        TreasureFeedback feedback;
        OceanLeaderboardBridge leaderboard;
        Camera view;
        Action changed;
        readonly List<Subject> subjects = new List<Subject>(240);
        readonly List<GameObject> animals = new List<GameObject>(140);
        readonly HashSet<Transform> registered = new HashSet<Transform>();
        readonly RaycastHit[] occluders = new RaycastHit[24];
        readonly OceanNaturalistObservation observation = new OceanNaturalistObservation();
        OceanLevelRun boundRun;
        int generation, cachedSubject = -1;
        bool configured, configuring, paused, focused = true;
        double nextSync, nextNotice, nextSelection;
        LineRenderer ring;
        Material ringMaterial;
        public OceanNaturalistStore Store { get; private set; }
        public OceanNaturalistState State => Store.State;
        public bool ObservationEnabled => Store.ObservationEnabled;

        public void Initialize(UnderwaterWorld owner, OceanCampaignController progress, TreasureHunt collection,
            Camera camera, HandSwimController tracking, TreasureFeedback presentation, OceanLeaderboardBridge ranking,
            string directory, Action refresh)
        {
            world = owner; campaign = progress; hunt = collection; view = camera; hands = tracking;
            feedback = presentation; leaderboard = ranking; changed = refresh;
            Store = new OceanNaturalistStore(directory);
            if (!Store.Writable) Debug.LogWarning("[Naturalist] " + Store.LastError + "; campaign data unaffected");
        }
        public void ToggleObservation()
        { if (Store.SetObservationEnabled(!ObservationEnabled)) { ResetObservation(); changed?.Invoke(); } }
        void LateUpdate()
        {
            if (Store == null || world == null || !world.IsReady || campaign == null || campaign.EnvironmentTransitioning ||
                !OceanCampaignAvailability.IsOpen(campaign.State.Current.level))
            {
                // Even a failed transition restores newly created shell/target models
                // under the same run object. Never retain their destroyed transforms.
                if (configured || configuring) InvalidateSubjects();
                ResetObservation(); return;
            }
            if (!ReferenceEquals(boundRun, campaign.State.Current) || (!configured && !configuring))
            {
                boundRun = campaign.State.Current; configured = false; generation++;
                StopAllCoroutines(); ResetObservation(); subjects.Clear(); registered.Clear(); cachedSubject = -1; nextSelection = 0;
                configuring = true;
                StartCoroutine(Configure(generation, boundRun.level));
            }
            // Upload recovery is independent of observation mode and of whether a menu is
            // open. A durable local record must still queue after a failed first attempt.
            if (!paused && focused && Time.unscaledTimeAsDouble >= nextSync)
            { nextSync = Time.unscaledTimeAsDouble + 15; SyncDiscoveries(); }
            if (!configured || !hunt.GameplayActive || OceanEntry.IsLoading || paused || !focused || !ObservationEnabled || !Store.Writable ||
                !feedback.CanShowExplorationMessage || Time.unscaledTimeAsDouble < nextNotice)
            { ResetObservation(); return; }
            int selected = SelectSubject();
            if (selected < 0) { ResetObservation(); return; }
            Subject subject = subjects[selected];
            bool complete = observation.Tick(selected, Time.unscaledDeltaTime, true);
            DrawRing(subject.Bounds(), observation.Progress);
            if (!complete) return;
            int species = subject.species;
            bool fresh = Store.TryRecord(species, campaign.State.Current.level, DateTimeOffset.UtcNow.ToString("O"));
            if (fresh || State.Contains(species))
            {
                var entry = OceanSpeciesCatalog.Get(species);
                string summary = entry.Description.Length > 36 ? entry.Description.Substring(0, 36) + "…" : entry.Description;
                feedback.TryShowExplorationMessage((fresh ? "新观察记录 · " : "生物观察 · ") + entry.Name +
                    $"（{State.AvailableCount}/{OceanSpeciesCatalog.AvailableCount}）\n" + summary);
                if (fresh) { leaderboard.RecordDiscovery(species, campaign.State.Current.level); changed?.Invoke(); }
            }
            else if (!string.IsNullOrEmpty(Store.LastError)) Debug.LogWarning("[Naturalist] " + Store.LastError);
            nextNotice = Time.unscaledTimeAsDouble + 12;
            ResetObservation();
        }
        IEnumerator Configure(int token, int level)
        {
            try { yield return ConfigureSubjects(token, level); }
            finally { if (token == generation) configuring = false; }
        }
        IEnumerator ConfigureSubjects(int token, int level)
        {
            yield return world.PrepareNaturalistHabitats(level, () => generation == token && campaign.State.Current == boundRun && world.IsReady);
            if (token != generation || !world.IsReady) yield break;
            world.GatherNaturalistAnimals(animals);
            foreach (GameObject animal in animals)
            {
                for (int id = 0; id < 15; id++)
                {
                    string path = OceanSpeciesCatalog.Get(id).ResourcePath;
                    string name = path.Substring(path.LastIndexOf('/') + 1);
                    if (animal.name.IndexOf(name, StringComparison.Ordinal) >= 0) { Add(animal, id, level); break; }
                }
            }
            foreach (var target in campaign.Creatures.Targets) Add(target.Visual, target.ObservationSpecies, level);
            foreach (var shell in hunt.Pickups) if (shell.kind >= 0) Add(shell.visual, 15 + shell.kind, level);
            foreach (OceanNaturalistTag tag in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanNaturalistTag>(true))
                Add(tag.gameObject, tag.Species, level);
            configured = true; nextSync = 0;
            Debug.Log($"[Naturalist] subjects={subjects.Count} catalog={OceanSpeciesCatalog.Count} level={level}; no schooling or per-frame resource loads");
        }
        void Add(GameObject model, int species, int level)
        {
            if (model == null || !OceanSpeciesCatalog.IsAvailable(species) || level < OceanSpeciesCatalog.Get(species).FirstLevel || !registered.Add(model.transform)) return;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0) subjects.Add(new Subject { model = model.transform, renderers = renderers, species = species });
        }
        int SelectSubject()
        {
            bool hasLeft = hands.TryGetMenuHandRay(HandType.HandLeft, out Ray left, out float lp) && lp < .2f && !hands.LeftGraspActive;
            bool hasRight = hands.TryGetMenuHandRay(HandType.HandRight, out Ray right, out float rp) && rp < .2f && !hands.RightGraspActive;
            if (!hasLeft && !hasRight) { cachedSubject = -1; return -1; }
            // Broad acquisition and environment raycasts run at 10 Hz. Between them,
            // validate only the selected moving model against the current hand rays.
            if (Time.unscaledTimeAsDouble >= nextSelection)
            {
                nextSelection = Time.unscaledTimeAsDouble + .1;
                cachedSubject = -1; float closest = 6;
                if (hasLeft) Find(left, ref cachedSubject, ref closest);
                if (hasRight) Find(right, ref cachedSubject, ref closest);
            }
            if (cachedSubject < 0 || cachedSubject >= subjects.Count) return -1;
            Subject subject = subjects[cachedSubject];
            if (subject.model == null || !subject.model.gameObject.activeInHierarchy) return -1;
            Bounds bounds = subject.Bounds(); bounds.Expand(.08f);
            bool leftHit = hasLeft && bounds.IntersectRay(left, out float ld) && ld >= 0 && ld < 6;
            bool rightHit = hasRight && bounds.IntersectRay(right, out float rd) && rd >= 0 && rd < 6;
            return leftHit || rightHit ? cachedSubject : -1;
        }
        void Find(Ray ray, ref int selected, ref float closest)
        {
            for (int i = 0; i < subjects.Count; i++)
            {
                Subject subject = subjects[i];
                if (subject.model == null || !subject.model.gameObject.activeInHierarchy) continue;
                Bounds bounds = subject.Bounds(); bounds.Expand(.08f);
                if (!bounds.IntersectRay(ray, out float distance) || distance < 0 || distance >= closest) continue;
                int count = Physics.RaycastNonAlloc(ray, occluders, Mathf.Max(0, distance - .04f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool blocked = count >= occluders.Length;
                for (int h = 0; !blocked && h < count; h++)
                {
                    Transform hit = occluders[h].transform;
                    if (hit != null && hit.IsChildOf(world.ActiveEnvironmentRoot) && !hit.IsChildOf(subject.model)) blocked = true;
                }
                if (blocked) continue;
                closest = distance; selected = i;
            }
        }
        void DrawRing(Bounds bounds, float progress)
        {
            if (ring == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/BubbleParticle");
                if (shader == null) return;
                ringMaterial = new Material(shader) { name = "Naturalist observation ring" };
                ringMaterial.SetColor("_BaseColor", new Color(.95f, .81f, .39f, .8f));
                var go = new GameObject("Hand-ray observation progress"); go.transform.SetParent(transform, false);
                ring = go.AddComponent<LineRenderer>(); ring.sharedMaterial = ringMaterial; ring.useWorldSpace = true;
                ring.startWidth = ring.endWidth = .003f; ring.positionCount = 33;
                ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
            }
            ring.enabled = true;
            float radius = Mathf.Clamp(bounds.extents.magnitude * .45f, .09f, .38f);
            Vector3 center = bounds.center + (view.transform.position - bounds.center).normalized * (bounds.extents.magnitude * .25f + .025f);
            for (int i = 0; i < 33; i++)
            {
                float angle = i / 32f * Mathf.PI * 2 * Mathf.Max(.02f, progress);
                ring.SetPosition(i, center + (view.transform.right * Mathf.Cos(angle) + view.transform.up * Mathf.Sin(angle)) * radius);
            }
        }
        void SyncDiscoveries()
        {
            for (int i = 0; i < State.Records.Count; i++)
            { var record = State.Records[i]; if (!leaderboard.RecordDiscovery(record.Species, record.FirstLevel)) break; }
        }
        void ResetObservation() { observation.Reset(); if (ring != null) ring.enabled = false; }
        void OnApplicationPause(bool value) { paused = value; if (value) ResetObservation(); }
        void OnApplicationFocus(bool value) { focused = value; if (!value) ResetObservation(); }
        void InvalidateSubjects()
        {
            generation++; StopAllCoroutines(); configuring = configured = false; boundRun = null;
            subjects.Clear(); registered.Clear(); cachedSubject = -1; nextSelection = 0;
        }
        void OnDisable() { InvalidateSubjects(); ResetObservation(); }
        void OnDestroy() { if (ringMaterial != null) { if (Application.isPlaying) Destroy(ringMaterial); else DestroyImmediate(ringMaterial); } }
    }
}
