using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>One controller for the static, grounded reef. Shared meshes/materials remain owned by their existing assets/build.</summary>
    [DisallowMultipleComponent]
    public sealed class ReefProximityLod : MonoBehaviour
    {
        sealed class Entry
        {
            public LODGroup group;
            public Bounds worldBounds;
            public bool remoteRidge,near;
            public int forced;
        }
        readonly List<Entry> entries=new List<Entry>(302);
        Camera playerCamera;
        int evaluatedFrame=-1;
        Vector3 evaluatedEye;
        public int RegisteredCount=>entries.Count;
        public int ForcedNearCount { get; private set; }
        public int UpgradedRemoteCount { get; private set; }
        public void Register(LODGroup group,Bounds worldBounds,bool remoteRidge)
        {
            if(group==null||group.lodCount<2)throw new ArgumentException("Reef proximity requires real LOD0 and an economical fallback.");
            var entry=new Entry{group=group,worldBounds=worldBounds,remoteRidge=remoteRidge,forced=remoteRidge?1:-1};
            group.ForceLOD(entry.forced);entries.Add(entry);if(remoteRidge)UpgradedRemoteCount++;
            evaluatedFrame=-1;
        }
        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;playerCamera=null;evaluatedFrame=-1;
            // Unity rejects ForceLOD while the environment hierarchy is disabled.
            // Reapply the reset policy when cached reef groups become active again.
            foreach(var e in entries)if(e.group!=null&&e.group.enabled&&e.group.gameObject.activeInHierarchy)e.group.ForceLOD(e.forced);
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            foreach(var e in entries){e.near=false;e.forced=e.remoteRidge?1:-1;if(e.group!=null&&e.group.enabled&&e.group.gameObject.activeInHierarchy)e.group.ForceLOD(e.forced);}
            ForcedNearCount=0;playerCamera=null;evaluatedFrame=-1;
        }
        void LateUpdate()
        {
            if(playerCamera==null||!playerCamera.isActiveAndEnabled||!playerCamera.CompareTag("MainCamera"))playerCamera=Camera.main;
            if(playerCamera!=null)Evaluate(playerCamera.transform.position);
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera==null||camera.cameraType!=CameraType.Game||!camera.CompareTag("MainCamera"))return;
            playerCamera=camera;
            // Runs before URP culling, using the current head position. FOV/lodBias changes
            // remain native outside the boost zone; inside it index zero is independent of both.
            Evaluate(camera.transform.position);
        }
        void Evaluate(Vector3 eye)
        {
            if(!Finite(eye))return;
            if(evaluatedFrame==Time.frameCount&&evaluatedEye==eye)return;
            evaluatedFrame=Time.frameCount;evaluatedEye=eye;int count=0;
            foreach(var e in entries)
            {
                if(e.group==null)continue;
                // Full 3D point-to-AABB distance: a large rock's nearby face qualifies even
                // if its centre is much farther away. Reefs never move after final Ground().
                int selected=ReefLodPolicy.SelectForcedLod(e.worldBounds.SqrDistance(eye),e.near,e.remoteRidge);
                e.near=selected==0;if(e.near)count++;
                if(selected!=e.forced){e.group.ForceLOD(selected);e.forced=selected;}
            }
            ForcedNearCount=count;
        }
        static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
#if UNITY_EDITOR
        public readonly struct Sample
        {
            public readonly LODGroup group;public readonly Bounds bounds;public readonly bool remoteRidge;public readonly int forcedLod;
            public Sample(LODGroup g,Bounds b,bool remote,int forced){group=g;bounds=b;remoteRidge=remote;forcedLod=forced;}
        }
        public Sample SampleForValidation(int index){var e=entries[index];return new Sample(e.group,e.worldBounds,e.remoteRidge,e.forced);}
        public void EvaluateForValidation(Vector3 eye){evaluatedFrame=-1;Evaluate(eye);}
#endif
    }
}
