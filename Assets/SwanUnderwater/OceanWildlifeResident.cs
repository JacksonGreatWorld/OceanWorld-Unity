using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed class OceanWildlifeResident : MonoBehaviour
    {
        UnderwaterWorld world;
        Vector3 home;
        float phase, radius, clearance;
        float cruiseSpeed=.22f, orbitAngularSpeed=.22f, verticalSway=.055f, blockedRetarget;
        OceanFishMotion motion;
        Animator[] nativeAnimators;
        RuntimeAnimatorController[] nativeControllers;
        float[] nativeBaseSpeeds, nativeAppliedSpeeds;
        bool[] nativeSpeedOwned;
        float nativeCadence = 1f;
        Transform ownedEnvironment;
        Camera playerView;
        HandSwimController playerHands;
        OceanCampaignController campaign;
        bool contextResolved;
        float proximityTimer, avoidanceRemaining, avoidanceCooldown;
        Vector3 avoidanceOffset, avoidanceTarget;
        readonly List<Bounds> nearbySolids=new List<Bounds>();
        public int CachedSolidCount => nearbySolids.Count;
        SkinnedMeshRenderer nativeSkin;
        Bounds nativeNavigationBounds;
        public void ConfigureNativeBounds(Bounds navigationBounds)
        {
            nativeSkin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (nativeSkin == null) throw new System.InvalidOperationException("Native resident skin is missing.");
            Vector3 scale=transform.lossyScale;
            float largestScale=Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            // The metadata already encloses all admitted poses in this navigation
            // root. Include an off-centre envelope instead of checking only a small
            // fraction of the fish's length at its navigation point.
            float bodyRadius=(navigationBounds.center.magnitude+navigationBounds.extents.magnitude)*largestScale;
            if(!Finite(bodyRadius)||bodyRadius<=0f)
                throw new System.ArgumentOutOfRangeException(nameof(navigationBounds),"Native body clearance must be finite and positive.");
            clearance=Mathf.Max(clearance,bodyRadius);
            nativeNavigationBounds = navigationBounds;
            CacheNearbySolids();
            ApplyNativeBounds();
        }
        void LateUpdate() => ApplyNativeBounds();
        public void ApplyNativeBounds()
        {
            if (nativeSkin != null)
                nativeSkin.bounds = OceanAuthoredFishGeometry.TransformBounds(nativeNavigationBounds, transform.localToWorldMatrix);
        }
        public void Initialize(UnderwaterWorld owner, float length, float orbit, int seed, bool authoredAnimation = false)
        {
            // A retry/rebind must return a previously borrowed playback speed before
            // taking a new baseline. Never rebind the controller or animation time.
            RestoreNativeCadence();
            world = owner; home = transform.position; radius = orbit; clearance = Mathf.Max(.12f, length * .18f);
            phase = seed * 1.37f;
            contextResolved=false;playerView=null;playerHands=null;campaign=null;
            avoidanceOffset=avoidanceTarget=Vector3.zero;avoidanceRemaining=avoidanceCooldown=proximityTimer=0f;
            ownedEnvironment=owner.ActiveEnvironmentRoot;
            CacheNearbySolids();
            CacheNativeCadence(authoredAnimation);
            motion = null;
            if (!authoredAnimation) motion = new OceanFishMotion(gameObject, length);
            else
            {
                Vector3 initial = OrbitOffset(phase);
                if (initial.sqrMagnitude > .000001f) transform.rotation = Quaternion.LookRotation(initial, Vector3.up);
            }
        }
        void CacheNearbySolids()
        {
            nearbySolids.Clear();
            if(world==null)return;
            // Static reef geometry is immutable after construction. Cache only the home
            // neighbourhood once instead of rescanning every reef in a 220 m world per fish.
            float extent=Mathf.Max(.5f,radius)*2.5f+clearance+1f;
            var neighbourhood=new Bounds(home,Vector3.one*extent*2f);
            foreach(Bounds solid in world.ActiveSolidBounds)
            {
                Bounds padded=solid;padded.Expand(clearance*2f);
                if(padded.Intersects(neighbourhood))nearbySolids.Add(padded);
            }
        }
        /// <summary>Configure once at spawn; no material instances or resource loads are created.</summary>
        public void ConfigureCruise(float speed, float angularSpeed=.22f, float verticalAmplitude=.055f)
        {
            if(!Finite(speed)||!Finite(angularSpeed)||!Finite(verticalAmplitude))
                throw new System.ArgumentOutOfRangeException("Resident cruise parameters must be finite.");
            cruiseSpeed=Mathf.Clamp(speed,.06f,1.8f);
            orbitAngularSpeed=Mathf.Clamp(angularSpeed,.04f,.8f);
            verticalSway=Mathf.Clamp(verticalAmplitude,0f,.35f);
        }
        void Update() => TickResident(Time.deltaTime);
        void TickResident(float elapsed)
        {
            if (world == null || !world.IsReady || ownedEnvironment == null || !ownedEnvironment.gameObject.activeInHierarchy) return;
            float dt = elapsed;
            if(!Finite(dt)||dt<=0f)return;
            dt=Mathf.Min(dt,.05f);phase += dt * orbitAngularSpeed;
            blockedRetarget=Mathf.Max(0f,blockedRetarget-dt);
            ResolvePlayerContext();
            bool readable=InteractionContextReadable();
            proximityTimer=Mathf.Max(0f,proximityTimer-dt);
            Vector3 threat=Vector3.zero;
            bool approaching=readable&&proximityTimer<=0f&&TryProximityThreat(out threat);
            if(readable&&proximityTimer<=0f)proximityTimer=.25f;
            StepAvoidance(dt,readable,approaching,threat);
            Vector3 desired = home + OrbitOffset(phase) + avoidanceOffset;
            // Both procedural and authored residents turn before moving. The animation
            // stays on its original visual child; the navigation wrapper owns the heading.
            Vector3 before = transform.position;
            Vector3 next=SteerForward(transform.position,transform.rotation,desired,dt,cruiseSpeed,out Quaternion heading);
            transform.rotation=heading;
            bool clear=IsClear(next);
            if(clear)transform.position=next;
            else if(blockedRetarget<=0f)
            {
                // Choose another point on this same fixed home orbit. Never push the
                // fish vertically through a reef or teleport it to the new target.
                phase+=Mathf.PI*.65f;blockedRetarget=.75f;
            }
            motion?.Tick(dt);
            TickNativeCadence(before, elapsed, dt);
        }
        void CacheNativeCadence(bool authored)
        {
            nativeAnimators = authored ? GetComponentsInChildren<Animator>(true) : null;
            int count = nativeAnimators == null ? 0 : nativeAnimators.Length;
            nativeControllers = count > 0 ? new RuntimeAnimatorController[count] : null;
            nativeBaseSpeeds = count > 0 ? new float[count] : null;
            nativeAppliedSpeeds = count > 0 ? new float[count] : null;
            nativeSpeedOwned = count > 0 ? new bool[count] : null;
            AcquireNativeCadence();
        }
        void AcquireNativeCadence()
        {
            RestoreNativeCadence();
            if (nativeAnimators == null) return;
            for (int i = 0; i < nativeAnimators.Length; i++)
            {
                Animator animator = nativeAnimators[i];
                if (animator == null || !animator.enabled || animator.runtimeAnimatorController == null ||
                    !Finite(animator.speed) || animator.speed < 0f) continue;
                nativeControllers[i] = animator.runtimeAnimatorController;
                nativeBaseSpeeds[i] = nativeAppliedSpeeds[i] = animator.speed;
                nativeSpeedOwned[i] = true;
            }
        }
        void TickNativeCadence(Vector3 before, float elapsed, float dt)
        {
            if (nativeAnimators == null) return;
            float distance = Vector3.Distance(before, transform.position);
            if (!Finite(distance)) return;
            // Use the translation that actually passed clearance, not the wanted
            // orbit speed. During a turn or a blocked step the authored swimmer
            // keeps a gentle positive stroke; successful travel restores its base.
            float relativeSpeed = Mathf.Clamp01(distance / elapsed / Mathf.Max(.06f, cruiseSpeed));
            float target = Mathf.Lerp(.42f, 1f, Mathf.SmoothStep(0f, 1f, relativeSpeed));
            nativeCadence = Mathf.Lerp(nativeCadence, target, 1f - Mathf.Exp(-dt * 4f));
            for (int i = 0; i < nativeAnimators.Length; i++)
            {
                Animator animator = nativeAnimators[i];
                if (!nativeSpeedOwned[i] || animator == null) continue;
                if (!animator.enabled || animator.runtimeAnimatorController != nativeControllers[i] ||
                    !Mathf.Approximately(animator.speed, nativeAppliedSpeeds[i]))
                {
                    // An explicit speed/controller writer takes precedence until a
                    // new enable/initialization boundary grants ownership again.
                    ReleaseNativeSpeed(i);
                    continue;
                }
                nativeAppliedSpeeds[i] = nativeBaseSpeeds[i] * nativeCadence;
                animator.speed = nativeAppliedSpeeds[i];
            }
        }
        void ReleaseNativeSpeed(int index)
        {
            if (nativeSpeedOwned == null || !nativeSpeedOwned[index]) return;
            Animator animator = nativeAnimators[index];
            if (animator != null && animator.runtimeAnimatorController == nativeControllers[index] &&
                Mathf.Approximately(animator.speed, nativeAppliedSpeeds[index]))
                animator.speed = nativeBaseSpeeds[index];
            nativeSpeedOwned[index] = false;
        }
        void RestoreNativeCadence()
        {
            if (nativeSpeedOwned != null)
                for (int i = 0; i < nativeSpeedOwned.Length; i++) ReleaseNativeSpeed(i);
            nativeCadence = 1f;
        }
        void OnEnable() => AcquireNativeCadence();
        void OnDestroy() => RestoreNativeCadence();
        void ResolvePlayerContext()
        {
            if(contextResolved)return;
            // Naturalist residents can be built before the campaign exists. Resolve
            // only after the world is ready; never search the scene in the hot loop.
            contextResolved=true;
            playerView=world.WildlifeView;playerHands=world.WildlifeHands;
            campaign=world.GetComponentInChildren<OceanCampaignController>(true);
        }
        bool InteractionContextReadable() => campaign!=null&&campaign.GameplayAllowed&&
            playerView!=null&&playerView.isActiveAndEnabled&&
            UnderwaterWorld.WildlifeHandResponsesAllowed(playerHands)&&playerHands.IsAnyHandActive();
        bool TryProximityThreat(out Vector3 threat)
        {
            threat=Vector3.zero;
            // No fresh hand means no interaction, rather than a false threat at the
            // world origin. A hand also qualifies the borrowed head context here.
            if(!playerHands.TryGetClosestActiveHand(transform.position,out Vector3 palm,out float palmDistance))return false;
            if(!Finite(palm)||!Finite(palmDistance))return false;
            float distance=Mathf.Clamp(clearance+.7f,.8f,2.1f);
            bool near=palmDistance<distance*distance;
            if(near)threat=palm;
            Vector3 head=playerView.transform.position;
            float headDistance=(head-transform.position).sqrMagnitude;
            if(Finite(head)&&headDistance<distance*distance&&(!near||headDistance<palmDistance))
            {near=true;threat=head;}
            return near;
        }
        void StepAvoidance(float elapsed,bool readable,bool approaching,Vector3 threat)
        {
            if(!Finite(elapsed)||elapsed<=0f)return;
            float dt=Mathf.Min(elapsed,.05f);
            avoidanceCooldown=Mathf.Max(0f,avoidanceCooldown-dt);
            avoidanceRemaining=Mathf.Max(0f,avoidanceRemaining-dt);
            if(!readable)
            {
                avoidanceRemaining=0f;avoidanceTarget=Vector3.zero;proximityTimer=0f;
            }
            else if(approaching&&Finite(threat)&&avoidanceRemaining<=0f&&avoidanceCooldown<=0f)
            {
                Vector3 away=Vector3.ProjectOnPlane(transform.position-threat,Vector3.up);
                if(away.sqrMagnitude<.0001f)away=-Vector3.ProjectOnPlane(transform.forward,Vector3.up);
                if(away.sqrMagnitude<.0001f)away=Vector3.back;
                away.Normalize();
                Vector3 side=Vector3.Cross(Vector3.up,away);
                if(Vector3.Dot(side,transform.forward)<0f)side=-side;
                avoidanceTarget=(side*.82f+away*.55f).normalized*Mathf.Clamp(radius*.25f,.25f,.85f);
                avoidanceRemaining=1.2f;avoidanceCooldown=4.2f;
            }
            if(avoidanceRemaining<=0f)avoidanceTarget=Vector3.zero;
            // Change the existing home-orbit goal, never the animal's position.
            // Its original nose-first steering and cached body clearance still gate travel.
            avoidanceOffset=Vector3.MoveTowards(avoidanceOffset,avoidanceTarget,dt*.7f);
        }
        void OnDisable()
        {
            RestoreNativeCadence();
            avoidanceOffset=avoidanceTarget=Vector3.zero;avoidanceRemaining=proximityTimer=0f;
            avoidanceCooldown=Mathf.Max(avoidanceCooldown,.75f);
        }
        bool IsClear(Vector3 next)
        {
            float homeLimit=Mathf.Max(.5f,radius)*2.5f;
            if((next-home).sqrMagnitude>homeLimit*homeLimit ||
                next.y<UnderwaterWorld.SampleRenderedSeabedHeight(next.x,next.z)+clearance)return false;
            for(int i=0;i<nearbySolids.Count;i++)if(nearbySolids[i].Contains(next))return false;
            return true;
        }
        Vector3 OrbitOffset(float atPhase) => new Vector3(Mathf.Sin(atPhase) * radius, Mathf.Sin(atPhase * .7f) * verticalSway, Mathf.Cos(atPhase) * radius * .55f);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);

        /// <summary>Authored swimmers turn first, then advance along their own nose. The owner still validates water/reef clearance.</summary>
        public static Vector3 SteerNativeForward(Vector3 position, Quaternion heading, Vector3 goal, float delta, out Quaternion nextHeading)
            => SteerForward(position,heading,goal,delta,.22f,out nextHeading);

        static Vector3 SteerForward(Vector3 position, Quaternion heading, Vector3 goal, float delta, float speed, out Quaternion nextHeading)
        {
            nextHeading = heading;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0f) return position;
            float dt = Mathf.Min(delta, .05f);
            Vector3 toward = goal - position; float distance = toward.magnitude;
            if (distance < .00001f) return position;
            Vector3 direction = toward / distance;
            nextHeading = Quaternion.RotateTowards(heading, Quaternion.LookRotation(direction, Vector3.up), dt * 28f);
            Vector3 forward = nextHeading * Vector3.forward;
            float alignment = Vector3.Dot(forward, direction);
            if (alignment <= .35f) return position;
            float travel = Mathf.Min(distance, speed * dt * alignment * alignment);
            return position + forward * travel;
        }
    }
}
