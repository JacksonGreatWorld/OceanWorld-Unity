using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld
    {
        // The bounded population plan spreads readable residents among fixed homes;
        // swimming never respawns, recentres or reconstructs the environment around the head.
        List<int>[] sanctuaryPocketGroups;
        readonly Dictionary<BoidSchool, Vector3> sanctuarySchoolHomes = new Dictionary<BoidSchool, Vector3>();
        readonly Dictionary<BoidSchool, SanctuaryPatrol> sanctuaryPatrols = new Dictionary<BoidSchool, SanctuaryPatrol>();
        int sanctuaryOpenSchoolCount;

        // Existing, authored swim corridors. The first open-water school really crosses the
        // arch; the remaining schools introduce life on the western, southern and eastern loops.
        static readonly int[] SanctuaryRouteStarts = { 1, 9, 13, 18 };
        static readonly int[] SanctuaryRouteEnds = { 3, 11, 16, 22 };
        static readonly Vector3 SanctuarySchoolHalfSize = new Vector3(2.35f, .85f, 1.65f);

        sealed class SanctuaryPatrol
        {
            public Vector3[] points;
            public float[] distances;
            public float length, travel, cruiseSpeed, spread;
            public Vector3 home, fleeDirection, velocity;
            public int profile;
        }

        bool BuildingSanctuary => (buildingEnvironment ?? activeEnvironment)?.kind == OceanEnvironmentKind.CoralSanctuary &&
            ConstructingBiome == OceanCampaignBiome.Reef;

        void PrepareSanctuaryLife()
        {
            if (!BuildingSanctuary) return;
            var zones = OceanCoralSanctuary.ExplorationZones;
            var habitat = ReefHabitat.Pockets;
            sanctuarySchoolHomes.Clear();
            sanctuaryPatrols.Clear();
            sanctuaryOpenSchoolCount = 0;
            sanctuaryPocketGroups = new List<int>[zones.Count];
            var routeDistances = new float[habitat.Count];
            for (int p = 0; p < habitat.Count; p++)
                routeDistances[p] = Mathf.Sqrt(FlatSquared(habitat[p].center, ClosestSanctuaryRoutePoint(habitat[p].center)));
            for (int z = 0; z < zones.Count; z++)
            {
                Vector3 center = ActiveSpawn + new Vector3(zones[z].offset.x, 0, zones[z].offset.y);
                var indices = new List<int>(habitat.Count);
                for (int p = 0; p < habitat.Count; p++)
                    if (routeDistances[p] <= 11f && FlatSquared(habitat[p].center,center) <= zones[z].radius * zones[z].radius)
                        indices.Add(p);
                // An authored zone without route-adjacent pockets uses clear-water placement,
                // never borrows a distant district's coral and silently empties this one.
                indices.Sort((a,b) => FlatSquared(habitat[a].center,center).CompareTo(FlatSquared(habitat[b].center,center)));
                // Twenty-four distinct nearby pockets prevent an entire large district from
                // using the same central dozen while its outer reef stays empty.
                if (indices.Count > 24) indices.RemoveRange(24,indices.Count-24);
                sanctuaryPocketGroups[z] = indices;
            }
        }

        static float FlatSquared(Vector3 a, Vector3 b)
        { float x=a.x-b.x,z=a.z-b.z;return x*x+z*z; }

        void PlaceSanctuaryHero(int index, float clearance, ref float x, ref float y, ref float z, ref int pocket, string speciesName = null)
        {
            if (!BuildingSanctuary || sanctuaryPocketGroups == null) return;
            int zone = OceanResidentDensityPolicy.HeroZone(index, sanctuaryPocketGroups.Length);
            int ordinal = OceanResidentDensityPolicy.HeroLocalOrdinal(index, sanctuaryPocketGroups.Length);
            var group = sanctuaryPocketGroups[zone];
            bool bottom = OceanResidentDensityPolicy.IsBottomResident(speciesName);
            // Bottom/grass fish keep their recognisable low-water habitat instead of
            // inheriting an arbitrary coral crown from the generic hero creation branch.
            if (bottom || ordinal % 3 == 0) pocket = -1;
            Vector3 at;
            if (pocket >= 0 && group.Count > 0)
            {
                pocket = group[Mathf.FloorToInt(Mathf.Repeat(ordinal * .61803399f + zone * .137f, 1f) * group.Count)];
                at = CoralTarget(pocket,index*2.39996f,false,clearance);
            }
            else
            {
                float speciesLift = bottom ? Mathf.Max(.28f, clearance + .12f) :
                    Mathf.Clamp(y-SampleTerrainHeight(x,z), Mathf.Max(.65f,clearance+.2f), 3.5f);
                Vector2 offset = OceanCoralSanctuary.ExplorationZones[zone].offset;
                float angle = ordinal*2.39996f + zone*.73f, radius = OceanResidentDensityPolicy.LocalRadius(ordinal);
                Vector3 preferred = ActiveSpawn+new Vector3(offset.x+Mathf.Cos(angle)*radius,0,
                    offset.y+Mathf.Sin(angle)*radius);
                Vector3 route = ClosestSanctuaryRoutePoint(preferred);
                Vector3 lateral = preferred-route; lateral.y=0;
                if (lateral.sqrMagnitude > 36f) preferred=route+lateral.normalized*6f;
                at=ClearSanctuaryWater(preferred,speciesLift,true);
            }
            x=at.x;y=at.y;z=at.z;
        }

        void PlaceSanctuarySchool(BoidSchool school, int index)
        {
            if (!BuildingSanctuary || sanctuaryPocketGroups == null) return;
            int zone=OceanResidentDensityPolicy.SchoolZone(index,sanctuaryPocketGroups.Length);
            var group=sanctuaryPocketGroups[zone];
            if (school.pocketIndex>=0 && group.Count>0)
            {
                int localStart = (index * 7 + zone * 3) % group.Count;
                school.pocketIndex=group[localStart];
                school.pocketIndices=new int[Mathf.Min(8,group.Count)];
                for (int member=0; member<school.pocketIndices.Length; member++)
                    school.pocketIndices[member]=group[(localStart+member)%group.Count];
                school.center=ReefHabitat.Pockets[school.pocketIndex].center+Vector3.up*.25f;
            }
            else
            {
                school.pocketIndex=-1;school.pocketIndices=null;
                Vector2 offset=OceanCoralSanctuary.ExplorationZones[zone].offset;
                school.center=ClearSanctuaryWater(ActiveSpawn+new Vector3(offset.x,0,offset.y),3.5f);
                sanctuarySchoolHomes[school]=school.center;
                int ordinal = sanctuaryOpenSchoolCount++;
                SanctuaryPatrol patrol = BuildSanctuaryPatrol(ordinal);
                if (patrol != null)
                {
                    sanctuaryPatrols.Add(school, patrol);
                    school.center = PatrolPoint(patrol, patrol.travel);
                    school.direction = PatrolHeading(patrol, patrol.travel);
                    school.speed = patrol.cruiseSpeed;
                    patrol.velocity = school.direction * school.speed;
                    sanctuarySchoolHomes[school] = patrol.home;
                }
            }
        }

        SanctuaryPatrol BuildSanctuaryPatrol(int ordinal)
        {
            int profile = ordinal % SanctuaryRouteStarts.Length;
            var authored = OceanCoralSanctuary.ExplorationRouteOffsets;
            int first = SanctuaryRouteStarts[profile], last = SanctuaryRouteEnds[profile];
            if (last >= authored.Count) return null;
            // Keep the same full formation envelope. Only the entrance may select a
            // small, fixed lateral/vertical offset to traverse the actual arch opening.
            float[] offsets = profile == 0 ? new[] { 0f, -.5f, .5f, -1f, 1f, -1.5f, 1.5f } : new[] { 0f };
            float[] lifts = profile == 0 ? new[] { 1.2f, 1f, 1.4f, 1.6f, 1.8f } : new[] { 2.2f };
            foreach (float lateral in offsets)
            foreach (float lift in lifts)
            {
                var candidates = new List<Vector3>();
                int runStart = 0, bestStart = 0, bestEnd = -1;
                float runLength = 0f, bestLength = 0f;
                bool previousClear = false;
                for (int segment = first; segment < last; segment++)
                {
                    Vector2 a = authored[segment], b = authored[segment + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                    for (int step = segment == first ? 0 : 1; step <= steps; step++)
                    {
                        Vector2 offset = Vector2.Lerp(a, b, step / (float)steps);
                        Vector3 point = ActiveSpawn + new Vector3(offset.x + lateral, 0f, offset.y);
                        point.y = SampleRenderedSeabedHeight(point.x, point.z) + lift;
                        int pointIndex = candidates.Count; candidates.Add(point);
                        bool clear = SanctuarySchoolWaterIsClear(point) &&
                            (pointIndex == 0 || SanctuarySchoolWaterIsClear((candidates[pointIndex - 1] + point) * .5f));
                        if (!clear) { previousClear = false; runLength = 0f; continue; }
                        if (!previousClear) { runStart = pointIndex; runLength = 0f; }
                        else runLength += Vector3.Distance(candidates[pointIndex - 1], point);
                        previousClear = true;
                        if (runLength <= bestLength) continue;
                        bestLength = runLength; bestStart = runStart; bestEnd = pointIndex;
                    }
                }
                if (bestLength < 6f || bestEnd <= bestStart) continue;
                if (profile == 0 && !(candidates[bestStart].z < OceanCoralSanctuary.ArchCenter.z - 2f &&
                    candidates[bestEnd].z > OceanCoralSanctuary.ArchCenter.z + 2f)) continue;
                var patrol = new SanctuaryPatrol
                {
                    profile = profile, points = new Vector3[bestEnd - bestStart + 1],
                    distances = new float[bestEnd - bestStart + 1],
                    cruiseSpeed = profile == 0 ? .92f : profile == 1 ? .58f : profile == 2 ? .48f : .72f
                };
                for (int i = 0; i < patrol.points.Length; i++)
                {
                    patrol.points[i] = candidates[bestStart + i];
                    if (i > 0) patrol.distances[i] = patrol.distances[i - 1] + Vector3.Distance(patrol.points[i - 1], patrol.points[i]);
                }
                patrol.length = patrol.distances[patrol.distances.Length - 1];
                patrol.travel = patrol.length * (.34f + ordinal % 3 * .12f);
                patrol.home = PatrolPoint(patrol, patrol.length * .5f);
                Debug.Log("[SanctuaryLife] fixed patrol " + profile + " points=" + patrol.points.Length +
                    " length_m=" + patrol.length.ToString("F1") + " cruise_mps=" + patrol.cruiseSpeed.ToString("F2") +
                    " lateral_m=" + lateral.ToString("F1") + " floor_lift_m=" + lift.ToString("F1") +
                    "; fixed corridor, full formation clearance and world root retained");
                return patrol;
            }
            if (profile == 0)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    Vector3 p = OceanCoralSanctuary.ArchCenter + Vector3.forward * dz;
                    p.y = SampleRenderedSeabedHeight(p.x, p.z) + 1.2f;
                    foreach (Bounds solid in ActiveSolidBounds)
                    {
                        Bounds padded = solid; padded.Expand(SanctuarySchoolHalfSize * 2f);
                        if (padded.Contains(p)) Debug.LogWarning("[SanctuaryLife] arch corridor blocker point=" + p.ToString("F2") +
                            " solid_center=" + solid.center.ToString("F2") + " solid_size=" + solid.size.ToString("F2"));
                    }
                }
            }
            Debug.LogWarning("[SanctuaryLife] patrol " + profile + " has no complete clear authored corridor; retaining fixed-home school behavior.");
            return null;
        }

        bool SanctuarySchoolWaterIsClear(Vector3 point)
        {
            // Clearance for the EXPECTED formation targets; individual fish can lag or overshoot
            // while steering. This conservative corridor test is not per-fish mesh collision proof.
            // Axis-aligned school offsets already use world space in the shared boid simulation.
            if (point.y - SanctuarySchoolHalfSize.y < SampleRenderedSeabedHeight(point.x, point.z) + .12f)
                return false;
            var solids = ActiveSolidBounds;
            for (int i = 0; i < solids.Count; i++)
            {
                Bounds padded = solids[i];
                padded.Expand(SanctuarySchoolHalfSize * 2f);
                if (padded.Contains(point)) return false;
            }
            return true;
        }

        static Vector3 PatrolPoint(SanctuaryPatrol patrol, float travel)
        {
            float along = Mathf.PingPong(travel, patrol.length);
            int low = 0, high = patrol.points.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (patrol.distances[middle] <= along) low = middle;
                else high = middle;
            }
            float segmentLength = patrol.distances[high] - patrol.distances[low];
            return Vector3.Lerp(patrol.points[low], patrol.points[high],
                segmentLength > .0001f ? (along - patrol.distances[low]) / segmentLength : 0f);
        }

        static Vector3 PatrolHeading(SanctuaryPatrol patrol, float travel)
        {
            Vector3 heading = PatrolPoint(patrol, travel + .5f) - PatrolPoint(patrol, travel);
            return heading.sqrMagnitude > .0001f ? heading.normalized : Vector3.forward;
        }

        // Hook once per school before the shared classic school update. A missing/trimmed
        // patrol returns false, so a malformed corridor never freezes an existing population.
        bool UpdateSanctuarySchool(BoidSchool school, int schoolIndex, Vector3 player, float dt)
        {
            if (!BuildingSanctuary || !sanctuaryPatrols.TryGetValue(school, out SanctuaryPatrol patrol)) return false;
            Vector3 threat = player;
            bool threatened = (school.center - player).sqrMagnitude < 2.2f * 2.2f;
            if (handController != null && handController.TryGetClosestActiveHand(school.center,
                out Vector3 hand, out float handDistance) && handDistance < 3f * 3f)
            { threat = hand; threatened = true; }
            StepSanctuaryPatrol(school, patrol, threat, threatened, dt);
            return true;
        }

        void StepSanctuaryPatrol(BoidSchool school, SanctuaryPatrol patrol, Vector3 threat, bool threatened, float elapsed)
        {
            // Retained environments can be paused or hidden for minutes. Neither wall-clock
            // time nor a resume spike is allowed to teleport their school centres along a path.
            float dt = Mathf.Clamp(elapsed, 0f, .05f);
            if (dt <= 0f) return;
            patrol.velocity = Vector3.zero;
            if (threatened)
            {
                // A hand above the group should send it sideways, not down into the seabed.
                Vector3 away = school.center - threat; away.y = 0f;
                patrol.fleeDirection = away.sqrMagnitude > .0001f ? away.normalized : -school.direction;
                school.scatterTimer = 1.1f;
            }
            else school.scatterTimer = Mathf.Max(0f, school.scatterTimer - dt);
            bool fleeing = school.scatterTimer > 0f;
            patrol.spread = Mathf.MoveTowards(patrol.spread, fleeing ? 1f : 0f, dt * (fleeing ? 3.5f : .55f));

            Vector3 routeTarget = PatrolPoint(patrol, patrol.travel);
            if (!fleeing && (routeTarget - school.center).sqrMagnitude < 2.5f * 2.5f)
                patrol.travel = Mathf.Repeat(patrol.travel + patrol.cruiseSpeed * dt, patrol.length * 2f);
            routeTarget = PatrolPoint(patrol, patrol.travel + .9f);
            Vector3 routeHeading = routeTarget - school.center;
            Vector3 desired = fleeing ? patrol.fleeDirection + routeHeading.normalized * .22f : routeHeading;
            if (fleeing) desired.y = Mathf.Max(0f, desired.y);
            if (desired.sqrMagnitude < .0001f) desired = PatrolHeading(patrol, patrol.travel);
            float targetSpeed = fleeing ? 2.8f : patrol.cruiseSpeed;
            school.speed = Mathf.MoveTowards(school.speed, targetSpeed, dt * (fleeing ? 3f : 1.1f));
            school.direction = Vector3.RotateTowards(school.direction, desired.normalized,
                dt * (fleeing ? 2.4f : 1.1f), 0f).normalized;
            Vector3 next = school.center + school.direction * (school.speed * dt);
            if (!SanctuarySchoolWaterIsClear(next))
            {
                // A flee response must not drive the group through a reef face. Steer back
                // into its prevalidated corridor; preserve position if both small steps block.
                school.scatterTimer = 0f;
                Vector3 back = (ClosestPatrolPoint(patrol, school.center) - school.center).normalized;
                if (back.sqrMagnitude < .0001f) back = routeHeading.normalized;
                next = school.center + back * (patrol.cruiseSpeed * dt);
                if (SanctuarySchoolWaterIsClear(next)) school.direction = back;
                else return;
            }
            // Feed the actual centre velocity to members. When an obstacle blocks the centre,
            // retaining its previous nominal speed would keep driving its members ahead.
            patrol.velocity = (next - school.center) / dt;
            school.center = next;
        }

        static Vector3 ClosestPatrolPoint(SanctuaryPatrol patrol, Vector3 point)
        {
            Vector3 closest = patrol.points[0];
            float distance = (closest - point).sqrMagnitude;
            for (int i = 1; i < patrol.points.Length; i++)
            {
                Vector3 a = patrol.points[i - 1], segment = patrol.points[i] - a;
                float fraction = segment.sqrMagnitude > .0001f ?
                    Mathf.Clamp01(Vector3.Dot(point - a, segment) / segment.sqrMagnitude) : 0f;
                Vector3 candidate = a + segment * fraction;
                float candidateDistance = (candidate - point).sqrMagnitude;
                if (candidateDistance >= distance) continue;
                distance = candidateDistance; closest = candidate;
            }
            return closest;
        }

        // Hook at the existing formationTarget expression. Desired offsets remain inside the
        // checked corridor envelope; steering lag/overshoot still needs rendered-route review.
        Vector3 SanctuaryFormationOffset(BoidSchool school, Vector3 offset)
        {
            if (!BuildingSanctuary || !sanctuaryPatrols.TryGetValue(school, out SanctuaryPatrol patrol)) return offset;
            float spread = patrol.spread;
            return new Vector3(offset.x * Mathf.Lerp(.62f, 1f, spread),
                offset.y * Mathf.Lerp(.72f, 1f, spread), offset.z * Mathf.Lerp(.58f, 1f, spread));
        }

        // Hook after the existing desired-velocity branch, before its shared smoothing. Keep
        // the original velocity untouched for classic schools, reef residents and failed routes.
        Vector3 SanctuaryBoidVelocity(BoidSchool school, Vector3 position, Vector3 formationTarget,
            Vector3 separation, Vector3 fallback)
        {
            if (!BuildingSanctuary || !sanctuaryPatrols.TryGetValue(school, out SanctuaryPatrol patrol)) return fallback;
            return SanctuaryPatrolMemberVelocity(patrol, position, formationTarget, separation);
        }

        static Vector3 SanctuaryPatrolMemberVelocity(SanctuaryPatrol patrol, Vector3 position,
            Vector3 formationTarget, Vector3 separation)
        {
            // Normalizing (alignment + cohesion) to the centre's speed erases small longitudinal
            // corrections: a fish already ahead can remain ahead indefinitely. Feed-forward
            // plus a bounded position correction allows both catching up and slowing down.
            Vector3 correction = Vector3.ClampMagnitude((formationTarget - position) * 1.1f, 1.25f);
            return Vector3.ClampMagnitude(patrol.velocity + correction + separation * .15f,
                patrol.velocity.magnitude + 1.25f);
        }

#if UNITY_EDITOR
        /// <summary>Behavior checks use detached school/state copies; live fish and RNG are not changed.</summary>
        public int CheckSanctuaryLifeForEditor()
        {
            int assertions = 0;
            void Check(bool condition, string message)
            {
                assertions++;
                if (!condition) throw new System.InvalidOperationException("Sanctuary life: " + message);
            }
            Check(IsReady && ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary, "ready sanctuary required");
            int openSchools = 0;
            foreach (BoidSchool school in boidSchools) if (school.pocketIndex < 0) openSchools++;
            Check(sanctuaryPatrols.Count == openSchools && openSchools > 0,
                "every configured open-water group has a clear fixed patrol");
            bool archCrossing = false;
            foreach (var entry in sanctuaryPatrols)
            {
                BoidSchool live = entry.Key;
                SanctuaryPatrol source = entry.Value;
                Check(boidSchools.Contains(live) && live.pocketIndex < 0, "only existing non-resident schools use patrols");
                Check(source.length >= 6f && source.points.Length > 1, "patrol has a continuous usable corridor");
                Check(sanctuarySchoolHomes.TryGetValue(live, out Vector3 home) && home == source.home,
                    "patrol home remains world anchored");
                for (int i = 0; i < source.points.Length; i++)
                {
                    Check(SanctuarySchoolWaterIsClear(source.points[i]), "expected formation targets clear solid bounds");
                    if (i > 0) Check(SanctuarySchoolWaterIsClear((source.points[i - 1] + source.points[i]) * .5f),
                        "corridor midpoints clear solid bounds");
                }
                if (source.profile == 0)
                {
                    Vector3 a = source.points[0], b = source.points[source.points.Length - 1];
                    archCrossing |= a.z < OceanCoralSanctuary.ArchCenter.z - 2f &&
                        b.z > OceanCoralSanctuary.ArchCenter.z + 2f;
                }
                // Build a detached copy, at its actual route, and exercise pause, a long resume
                // spike, flee and recovery. No scene transforms or resident school states change.
                var copy = new SanctuaryPatrol
                {
                    points = source.points, distances = source.distances, length = source.length,
                    travel = source.length * .34f, cruiseSpeed = source.cruiseSpeed,
                    profile = source.profile, home = source.home
                };
                var school = new BoidSchool
                {
                    center = PatrolPoint(copy, copy.travel), direction = PatrolHeading(copy, copy.travel),
                    speed = copy.cruiseSpeed
                };
                Vector3 paused = school.center;
                float pausedTravel = copy.travel;
                StepSanctuaryPatrol(school, copy, Vector3.zero, false, 0f);
                Check(school.center == paused && copy.travel == pausedTravel, "paused state does not advance");
                StepSanctuaryPatrol(school, copy, Vector3.zero, false, 30f);
                Check(Vector3.Distance(paused, school.center) <= .141f,
                    "resume spike cannot jump school centre beyond one capped step");
                float greatestSpread = 0f;
                for (int frame = 0; frame < 720; frame++)
                {
                    Vector3 before = school.center;
                    bool threatened = frame < 30;
                    Vector3 hand = school.center + Vector3.right * .5f;
                    StepSanctuaryPatrol(school, copy, hand, threatened, 1f / 60f);
                    greatestSpread = Mathf.Max(greatestSpread, copy.spread);
                    Check(Vector3.Distance(before, school.center) <= 2.8f / 60f + .0001f,
                        "flee/recovery never teleports a group");
                    Check(SanctuarySchoolWaterIsClear(school.center), "flee/recovery retains reef clearance");
                    Check(!float.IsNaN(school.direction.x) && Mathf.Abs(school.direction.sqrMagnitude - 1f) < .001f,
                        "direction remains finite and normalized");
                }
                Check(greatestSpread > .5f && copy.spread < .001f && school.scatterTimer <= 0f,
                    "nearby player/hand produces a temporary spread, then regroups");
                Check(Vector3.Distance(school.center, ClosestPatrolPoint(copy, school.center)) < 2.5f,
                    "recovered group returns to its original patrol corridor");
                Check(copy.home == source.home, "flee and recovery do not recenter the home");

                // A member initially ahead must converge, not preserve its lead because both
                // centre and fish are normalized to the same forward speed. Reuse the real
                // two-frame steering cadence and shared velocity smoothing on detached values.
                copy.velocity = Vector3.forward * source.cruiseSpeed;
                Vector3 target = Vector3.zero, position = Vector3.forward * 3f;
                Vector3 velocity = copy.velocity;
                for (int frame = 0; frame < 300; frame++)
                {
                    target += copy.velocity / 60f;
                    if (frame % 2 == 0)
                    {
                        Vector3 desiredVelocity = SanctuaryPatrolMemberVelocity(copy, position, target, Vector3.zero);
                        velocity = Vector3.Lerp(velocity, desiredVelocity, 1f - Mathf.Exp(-6f / 60f));
                    }
                    position += velocity / 60f;
                }
                Check((position - target).magnitude < .15f,
                    "member ahead of a moving centre converges with the existing staggered smoothing");
            }
            Check(archCrossing, "entrance school crosses both sides of the authored reef arch");
            return assertions;
        }
#endif

        Vector3 ClosestSanctuaryRoutePoint(Vector3 point)
        {
            Vector3 closest = ActiveSpawn; float best = float.PositiveInfinity;
            void Scan(IReadOnlyList<Vector2> route)
            {
                for (int i=1; i<route.Count; i++)
                {
                    Vector3 a=ActiveSpawn+new Vector3(route[i-1].x,0,route[i-1].y);
                    Vector3 b=ActiveSpawn+new Vector3(route[i].x,0,route[i].y);
                    Vector3 segment=b-a, offset=point-a; offset.y=0;
                    Vector3 candidate=a+segment*Mathf.Clamp01(Vector3.Dot(offset,segment)/Mathf.Max(.0001f,segment.sqrMagnitude));
                    float distance=FlatSquared(point,candidate);
                    if(distance<best){best=distance;closest=candidate;}
                }
            }
            Scan(OceanCoralSanctuary.ExplorationRouteOffsets);
            Scan(OceanCoralSanctuary.ExplorationShortcutOffsets);
            return closest;
        }

        Vector3 ClearSanctuaryWater(Vector3 preferred, float lift, bool nearRoute=false)
        {
            for(int attempt=0;attempt<18;attempt++)
            {
                float angle=attempt*2.39996f,radius=attempt==0?0:1.6f+attempt*.65f;
                Vector3 point=preferred+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                point.y=SampleRenderedSeabedHeight(point.x,point.z)+lift;
                if(nearRoute && FlatSquared(point,ClosestSanctuaryRoutePoint(point))>64f)continue;
                bool blocked=false;
                foreach(Bounds solid in ActiveSolidBounds)
                {
                    Bounds padded=solid;padded.Expand(.8f);
                    if(padded.Contains(point)){blocked=true;break;}
                }
                if(!blocked)return point;
            }
            // A safe open-water fallback above the local solid bounds, not a random distant teleport.
            preferred.y=SampleRenderedSeabedHeight(preferred.x,preferred.z)+lift;
            foreach(Bounds solid in ActiveSolidBounds)
                if(preferred.x>=solid.min.x-.4f&&preferred.x<=solid.max.x+.4f&&
                    preferred.z>=solid.min.z-.4f&&preferred.z<=solid.max.z+.4f)
                    preferred.y=Mathf.Max(preferred.y,solid.max.y+1f);
            return preferred;
        }

        [System.Serializable] public sealed class ResidentDensitySnapshot
        {
            public string environment;
            public int heroCount, schoolCount, schoolingFishCount;
            public int[] heroesByZone, schoolsByZone;
            public ResidentSpeciesCount[] species;
            public float maximumHeroRouteDistance;
            public int heroesWithinEightMetresOfRoute;
        }
        [System.Serializable] public sealed class ResidentSpeciesCount
        {
            public int zone, count;
            public string resourceName;
        }
        /// <summary>Explicit diagnostic snapshot of live roots; no per-frame allocations or collection-state changes.</summary>
        public ResidentDensitySnapshot GetResidentDensitySnapshot()
        {
            int zones=ActiveEnvironment==OceanEnvironmentKind.CoralSanctuary?OceanCoralSanctuary.ExplorationZones.Count:1;
            var snapshot=new ResidentDensitySnapshot {
                environment=ActiveEnvironment.ToString(), heroesByZone=new int[zones], schoolsByZone=new int[zones],
                schoolCount=boidSchools.Count, schoolingFishCount=boids.Count
            };
            int Zone(Vector3 point)
            {
                if(zones==1)return 0;
                int result=0;float nearest=float.PositiveInfinity;
                for(int z=0;z<zones;z++)
                {
                    Vector2 offset=OceanCoralSanctuary.ExplorationZones[z].offset;
                    float distance=FlatSquared(point,ActiveSpawn+new Vector3(offset.x,0,offset.y));
                    if(distance<nearest){nearest=distance;result=z;}
                }
                return result;
            }
            var counts=new Dictionary<string,ResidentSpeciesCount>();
            if(fishRoot!=null)foreach(Transform fish in fishRoot)
            {
                int zone=Zone(fish.position);snapshot.heroCount++;snapshot.heroesByZone[zone]++;
                string resource=fish.name;
                if(resource.StartsWith("Fish_",System.StringComparison.Ordinal))resource=resource.Substring(5);
                int suffix=resource.LastIndexOf('_');
                if(suffix>=0 && int.TryParse(resource.Substring(suffix+1),out _))resource=resource.Substring(0,suffix);
                string key=zone+":"+resource;
                if(!counts.TryGetValue(key,out ResidentSpeciesCount entry))
                {entry=new ResidentSpeciesCount{zone=zone,resourceName=resource};counts.Add(key,entry);}
                entry.count++;
                if(zones>1)
                {
                    float distance=Mathf.Sqrt(FlatSquared(fish.position,ClosestSanctuaryRoutePoint(fish.position)));
                    snapshot.maximumHeroRouteDistance=Mathf.Max(snapshot.maximumHeroRouteDistance,distance);
                    if(distance<=8f)snapshot.heroesWithinEightMetresOfRoute++;
                }
            }
            foreach(BoidSchool school in boidSchools)snapshot.schoolsByZone[Zone(school.center)]++;
            var entries=new List<ResidentSpeciesCount>(counts.Values);
            entries.Sort((a,b)=>a.zone!=b.zone?a.zone.CompareTo(b.zone):string.CompareOrdinal(a.resourceName,b.resourceName));
            snapshot.species=entries.ToArray();return snapshot;
        }

        Vector3 SchoolBoundaryOffset(BoidSchool school, Vector3 player)
        {
            // Classic keeps its current player-area rule. Sanctuary schools stay in their
            // own coves, so visiting another region actually reveals another population.
            Vector3 offset=school.center-(BuildingSanctuary&&sanctuarySchoolHomes.TryGetValue(school,out Vector3 home)?home:player);
            offset.y=0;return offset;
        }
    }
}
