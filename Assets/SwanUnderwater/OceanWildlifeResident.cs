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
        Transform ownedEnvironment;
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
            world = owner; home = transform.position; radius = orbit; clearance = Mathf.Max(.12f, length * .18f);
            phase = seed * 1.37f;
            ownedEnvironment=owner.ActiveEnvironmentRoot;
            CacheNearbySolids();
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
        void Update()
        {
            if (world == null || !world.IsReady || ownedEnvironment == null || !ownedEnvironment.gameObject.activeInHierarchy) return;
            float dt = Time.deltaTime;
            if(!Finite(dt)||dt<=0f)return;
            dt=Mathf.Min(dt,.05f);phase += dt * orbitAngularSpeed;
            blockedRetarget=Mathf.Max(0f,blockedRetarget-dt);
            Vector3 desired = home + OrbitOffset(phase);
            // Both procedural and authored residents turn before moving. The animation
            // stays on its original visual child; the navigation wrapper owns the heading.
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
