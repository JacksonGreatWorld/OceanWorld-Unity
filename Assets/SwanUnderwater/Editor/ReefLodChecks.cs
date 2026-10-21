#if UNITY_EDITOR || OCEAN_REEF_LOD_STANDALONE
using System;
#if UNITY_EDITOR
using UnityEngine;
#endif
namespace SwanUnderwater.EditorTests
{
    public static class ReefLodChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool ok,string message){Assertions++;if(!ok)throw new InvalidOperationException("Reef LOD: "+message);}
        public static void RunPolicyTests()
        {
            Assertions=0;
            foreach(bool remote in new[]{false,true})
            {
                int fallback=remote?1:-1;
                foreach(float squared in new[]{0f,1f,63.99f,64f})Check(ReefLodPolicy.SelectForcedLod(squared,false,remote)==0,"approach within eight metres uses actual LOD0");
                foreach(float squared in new[]{64.01f,80f,81f,10000f})Check(ReefLodPolicy.SelectForcedLod(squared,false,remote)==fallback,"outside entering zone keeps economical fallback");
                foreach(float squared in new[]{0f,64f,72.25f,80.99f})Check(ReefLodPolicy.SelectForcedLod(squared,true,remote)==0,"8–9m retreat hysteresis retains high detail");
                foreach(float squared in new[]{81f,10000f,float.NaN,float.PositiveInfinity,-1f})Check(ReefLodPolicy.SelectForcedLod(squared,true,remote)==fallback,"nine metres or invalid sample exits safely");
                bool near=false;
                foreach(float distance in new[]{20f,8.1f,7.9f,8.2f,7.95f,8.8f,9.1f,8.2f,7.8f})
                {
                    int selected=ReefLodPolicy.SelectForcedLod(distance*distance,near,remote);
                    if(distance<=8f)Check(selected==0,"walking back restores LOD0");
                    if(distance>=9f)Check(selected==fallback,"walking away releases boost");
                    near=selected==0;
                }
            }
#if UNITY_EDITOR
            Debug.Log("[ReefLodChecks] policy PASS "+Assertions+" assertions.");
#else
            Console.WriteLine("[ReefLodChecks] policy PASS "+Assertions+" assertions.");
#endif
        }
#if UNITY_EDITOR
        public static void ValidateBuilt(ReefProximityLod reef,Vector3 returnEye)
        {
            if(reef==null)throw new ArgumentNullException(nameof(reef));
            Check(reef.RegisteredCount==302,"all 250 coral and 52 rock groups registered");
            Check(reef.UpgradedRemoteCount==22,"all 22 previously far-only ridges receive high geometry");
            var farAway=new Vector3(10000,10000,10000);int remoteCount=0;
            try
            {
                for(int i=0;i<reef.RegisteredCount;i++)
                {
                    var sample=reef.SampleForValidation(i);LOD[] lods=sample.group.GetLODs();
                    Check(lods.Length>=2&&lods[0].renderers.Length>0&&lods[1].renderers.Length>0,"actual high and low renderer arrays "+i);
                    long high=Triangles(lods[0].renderers),low=Triangles(lods[1].renderers);
                    Check(high>low&&low>0,"LOD0 has more real triangles than LOD1 "+i);
                    foreach(var r in lods[0].renderers)Check(Array.IndexOf(lods[1].renderers,r)<0,"LOD0 is not an alias for low renderer "+i);
                    reef.EvaluateForValidation(farAway);
                    Check(reef.SampleForValidation(i).forcedLod==(sample.remoteRidge?1:-1),"return to original economical fallback "+i);
                    Vector3 surface=new Vector3(sample.bounds.max.x,sample.bounds.center.y,sample.bounds.center.z);
                    reef.EvaluateForValidation(surface+Vector3.right*7.99f);
                    Check(reef.SampleForValidation(i).forcedLod==0,"walking to any reef surface restores real LOD0 "+i);
                    reef.EvaluateForValidation(surface+Vector3.right*8.5f);Check(reef.SampleForValidation(i).forcedLod==0,"no seam chatter at 8.5m "+i);
                    reef.EvaluateForValidation(surface+Vector3.right*9.01f);Check(reef.SampleForValidation(i).forcedLod==(sample.remoteRidge?1:-1),"release after nine metres "+i);
                    reef.EvaluateForValidation(surface+Vector3.right*.5f);Check(reef.SampleForValidation(i).forcedLod==0,"second approach is not locked to original spawn distance "+i);
                    if(sample.remoteRidge)remoteCount++;
                }
                Check(remoteCount==22,"every remote ridge participates in repeated approach checks");
                reef.EvaluateForValidation(farAway);Check(reef.ForcedNearCount==0,"far viewpoint never forces whole scene to LOD0");
                Check(ReefHabitat.LastGrounding.unanchoredLods==0,"all real geometry LOD contacts remain grounded");
            }
            finally{reef.EvaluateForValidation(farAway);reef.EvaluateForValidation(returnEye);}
            Debug.Log("[ReefLodChecks] actual habitat PASS "+Assertions+" assertions; camera rendering/device acceptance separate.");
        }
        public static long Triangles(Renderer[] renderers)
        {
            long count=0;
            foreach(Renderer r in renderers)
            {
                Mesh mesh=r is SkinnedMeshRenderer skin?skin.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null)continue;for(int sub=0;sub<mesh.subMeshCount;sub++)if(mesh.GetTopology(sub)==MeshTopology.Triangles)count+=(long)mesh.GetIndexCount(sub)/3;
            }
            return count;
        }
#endif
    }
}
#endif
