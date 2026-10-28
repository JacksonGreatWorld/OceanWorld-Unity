#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Explicit, transactional admission of two licensed native rigs. No load/build/import hooks.
    /// Does not spawn animals, register observations, switch build targets or operate a headset.</summary>
    public static class OceanMarineCompanionBake
    {
        const string Sources = "Assets/ArtSources/MarineCompanions";
        const string Baked = "Assets/ArtBaked/MarineCompanions";
        const string Credits = "Assets/StreamingAssets/OceanMarineCompanions-credits.txt";
        const string Portable = "Sources/MarineExpansion/marine-companions-native-admission.json";
        const string DecoderPath = "Assets/SwanUnderwater/Editor/ReefAstcReadback.shader";
        static readonly Spec[] Species = {
            new Spec { id="sea-turtle", package="Sources/MarineExpansion/sea-turtle-23dcb315", uid="23dcb315dea44f5082b020b04710bd31",
                title="Sea Turtle", author="Eloi (@Eloiart)", url="https://sketchfab.com/3d-models/sea-turtle-23dcb315dea44f5082b020b04710bd31",
                archiveBytes=151790841, archiveHash="0fb2bc104388e8494787ca1b16b78b304002a920054e2cf048cf887b0fd9366b",
                model="PBR_turtle.fbx", color="BaseColor_4K.png", normal="NormalMap_4K.png", roughness="Roughness_4K.png", resolution=4096,
                head="Head.003", tail="ArmBack.L.001", tailOther="ArmBack.R.001", fixedBone="Bone", fixedAxis="m_LocalPosition.y", targetDiagonal=1f, loopStart=98f/24f, loopDuration=110f/24f,
                inputs=new[] {
                    new Input("extracted/source/PBR_turtle.fbx",540556,"66314580c1fa20ab08dad394345d1b79e36cc9eed31984b4b1e2c9b2809534d2"),
                    new Input("extracted/textures/BaseColor_4K.png",50416589,"21a3c8668ec4eca552cc5b1b9a31816c7b33bc668f824cbba3eef19673e9b0b2"),
                    new Input("extracted/textures/NormalMap_4K.png",50416589,"50e0ad5dfd8805b5ff13dd169363469ccbca439daa6f0a64c8b1c90a6a161da0"),
                    new Input("extracted/textures/Roughness_4K.png",50416589,"5978e46dbb9d8953656333546ef5516f6bae5e494a9d9d2b68aef624c720a1a7") } },
            new Spec { id="dolphin", package="Sources/MarineExpansion/dolphin-b621ec88", uid="b621ec8894734bc79c04338edff3cd0d",
                title="Game-Ready dolphin (Swimming)", author="Allie2k", url="https://sketchfab.com/3d-models/game-ready-dolphin-swimming-b621ec8894734bc79c04338edff3cd0d",
                archiveBytes=54945559, archiveHash="588124a93b56bbba4e0f567280cb3fd8aab4993ae2a622df958ef8433b847052",
                model="DolphinanimTest_02.fbx", color="export_initialShadingGroup_BaseColor.png", normal="export_initialShadingGroup_Normal.png", resolution=2048,
                head="face", tail="tailEnd", targetDiagonal=1.65f, loopStart=8f/24f, loopDuration=42f/24f,
                inputs=new[] {
                    new Input("extracted/nested/DolphinanimTest_02.fbx",25614944,"60f535e98f4c44197561c7c535f87c6050326746b3a854455c83745a5c727bb3"),
                    new Input("extracted/textures/export_initialShadingGroup_BaseColor.png",2049961,"bb2b87f6b4d875ed9442e94ad831e03e664c3aeca86d5c7cfe1169e2bd3b1527"),
                    new Input("extracted/textures/export_initialShadingGroup_Normal.png",2113748,"78b7fd00a3abe5b2e33d1a98096ede3f854353b28dc79345c90a1b828ee53e7c") } }
        };
        static readonly string[] Protected = { "Assets/Resources/Fish", "Assets/Resources/RealisticReef", "Assets/ArtBaked/MarketReef", "Assets/ArtBaked/ReefAstc",
            "Assets/ArtSources/MarketReef", "Assets/ArtBaked/MarineExpansion/mackerel", "Assets/ArtSources/MarineExpansion/mackerel",
            "Assets/Resources/MarineExpansion/mackerel.prefab", "Assets/Resources/MarineExpansion/mackerel.prefab.meta",
            "Assets/Resources/MarineExpansion/mackerel-geometry.json", "Assets/Resources/MarineExpansion/mackerel-geometry.json.meta" };
        sealed class Spec {
            public string id,package,uid,title,author,url,archiveHash,model,color,normal,roughness,head,tail,tailOther,fixedBone,fixedAxis;
            public int resolution; public long archiveBytes; public float targetDiagonal,loopStart,loopDuration; public Input[] inputs;
            public string SourceRoot => Sources+"/"+id;
            public string BakedRoot => Baked+"/"+id;
            public string ModelPath => SourceRoot+"/"+model;
            public string PrefabPath => "Assets/Resources/MarineExpansion/"+id+".prefab";
            public string GeometryPath => "Assets/Resources/MarineExpansion/"+id+"-geometry.json";
        }
        sealed class Input { public string path,hash; public long bytes; public Input(string path,long bytes,string hash) { this.path=path;this.bytes=bytes;this.hash=hash; } }
        [Serializable] sealed class Receipt { public string uid,source_url,author,license,license_url,sha256,license_metadata_file,license_metadata_sha256; public long bytes; public Roster[] roster; public Nested nested_archive; }
        [Serializable] sealed class Roster { public string path; public long bytes,crc; }
        [Serializable] sealed class Nested { public string path; public Roster[] roster; }
        [Serializable] public sealed class FileRecord { public string path,sha256; public long bytes; }
        [Serializable] public sealed class TextureRecord {
            public string path,role,sourceSha256,format,graphicsFormat; public int width,height,mips,copyError,maximumError; public long runtimeBytes;
            public bool srgb; public double meanByteError,rmsByteError,meanNormalAngle,p95NormalAngle,maxNormalAngle;
        }
        [Serializable] public sealed class PositionEdit { public string path,property; public float originalStart,originalEnd,originalRange,constant; public int originalKeys; }
        [Serializable] public sealed class BoneRecord { public string path; public Vector3 firstPosition; public float loopAngle; }
        [Serializable] public sealed class PoseRecord { public float time; public Vector3 min,max; }
        [Serializable] public sealed class AnimalReport {
            public string id,sourceUrl,author,license="CC-BY-4.0",modelSha256,sourceClip,derivedClip,prefabPath,geometryPath,headingPolicy;
            public float clipLength,sourceCycleStart,sourceCycleDuration,sampleEnd,sourceDiagonal,targetDiagonal,actualLength,noseAgreement,maxVertexTravel,maxHeadTravel,maxCenterTravel,maxPoseDiagonal,loopVertexGap,loopBoneAngle,rootTravel;
            public int vertices,bones,bindposes,poseSamples; public long triangles,runtimeTextureBytes;
            public Vector3 sourceMin,sourceMax,sourceNose,sourceUp,headingEuler,navigationMin,navigationMax,skinLocalScale,skinLossyScale;
            public List<PositionEdit> positionEdits=new List<PositionEdit>(); public List<BoneRecord> boneRoster=new List<BoneRecord>();
            public List<PoseRecord> poses=new List<PoseRecord>(); public List<TextureRecord> textures=new List<TextureRecord>();
        }
        [Serializable] public sealed class Report {
            public bool publicationProjection;
            public string sourceReportSha256, publicationScope;
            public int schemaVersion=1; public string tool="OceanMarineCompanionBake",utc,unity,directory,state,error;
            public string boundary="Native animated resource admission only. CPU posed URP stills are not runtime/headset animation acceptance. Turtle and dolphin are not fish or new catalog discoveries.";
            public bool success; public int finalizationTicks; public List<AnimalReport> animals=new List<AnimalReport>();
            public List<FileRecord> protectedFiles=new List<FileRecord>(),sourceFiles=new List<FileRecord>(),after=new List<FileRecord>();
            public List<string> plannedPaths=new List<string>();
        }
        sealed class Pending { public Report report;public Exception error;public List<FileRecord> last; public int stable,ticks; }
        static Pending pending;
        static Material decoder;
        static bool calibrated,flipRows;
        static string EvidenceRoot => Path.GetFullPath(Argument("-oceanCompanionBakeOutput",Environment.GetEnvironmentVariable("OCEAN_COMPANION_BAKE_REPORT_DIR")??"Validation/marine-companion-bake"));
        static string Latest => Path.Combine(EvidenceRoot,"latest.json");

        [MenuItem("Swan/Codex/Marine Expansion/Admit original animated turtle and dolphin")]
        public static void Run()
        {
            Report report=null;
            try {
                Require(pending==null&&!EditorApplication.isPlayingOrWillChangePlaymode,"Idle Edit Mode required.");
                Require(EditorUserBuildSettings.activeBuildTarget==BuildTarget.Android,"Android must already be active; no project settings are changed.");
                Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null&&SystemInfo.supportsAsyncGPUReadback,"Graphics-capable Editor with GPU readback required.");
                Require(PlayerSettings.GetNormalMapEncoding(UnityEditor.Build.NamedBuildTarget.Android)==NormalMapEncoding.DXT5nm,"Existing Android DXT5nm normal policy required.");
                Require(!Environment.GetCommandLineArgs().Contains("-quit"),"Do not use -quit: deferred material writes must stabilize.");
                foreach (Spec spec in Species) VerifySource(spec);
                if (OwnedFiles().Any()) {
                    Report old=ReadReport();Require(old.success&&old.state=="admitted","Incomplete/failed owned assets exist; inspect evidence then explicitly Rollback.");
                    VerifyFiles(old.after);Require(Same(old.after,Snapshot(OwnedFiles())),"Unknown files in the owned namespace.");ValidateOutputs(old);
                    Debug.Log("MARINE_COMPANION_BAKE_UNCHANGED "+Latest);Exit(0);return;
                }
                Directory.CreateDirectory(EvidenceRoot);
                report=new Report {utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,state="preparing",
                    directory=Path.Combine(EvidenceRoot,"companions-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"))};
                report.protectedFiles=Snapshot(Protected.SelectMany(FilesUnderOrSelf));
                foreach (Spec spec in Species) {
                    report.sourceFiles.AddRange(Snapshot(new[]{spec.package+"/source-original.zip",spec.package+"/receipt.json"}.Concat(spec.inputs.Select(x=>spec.package+"/"+x.path))));
                }
                Save(report);MakeFolder(Sources);MakeFolder(Baked);MakeFolder("Assets/Resources/MarineExpansion");MakeFolder("Assets/StreamingAssets");
                CalibrateReadback();
                foreach(Spec spec in Species) Admit(spec,report);
                Plan(Credits,report);File.WriteAllText(Credits,string.Join("\n\n",Species.Select(x=>x.title+" by "+x.author+"\n"+x.url+"\nLicensed CC BY 4.0 — https://creativecommons.org/licenses/by/4.0/\nChanges: URP materials, native ASTC4x4 maps, static anatomical orientation/scale, looping controller"+(x.fixedBone==null?".":"; explicit in-place body translation curve derived from the original.")))+"\n");
                AssetDatabase.ImportAsset(Credits,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();ValidateOutputs(report);BeginFinalize(report,null);
            } catch(Exception ex) { Debug.LogException(ex);if(report!=null)BeginFinalize(report,ex);else {ReleaseReadback();Exit(1);if(!Application.isBatchMode)throw;} }
        }

        static void Admit(Spec spec,Report report)
        {
            var result=new AnimalReport {id=spec.id,sourceUrl=spec.url,author=spec.author,prefabPath=spec.PrefabPath,geometryPath=spec.GeometryPath,targetDiagonal=spec.targetDiagonal};
            report.animals.Add(result);MakeFolder(spec.SourceRoot);MakeFolder(spec.BakedRoot);
            foreach(Input input in spec.inputs) { string target=spec.SourceRoot+"/"+Path.GetFileName(input.path);Plan(target,report);File.Copy(spec.package+"/"+input.path,target);Require(Hash(target)==input.hash,"Staged source changed."); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);ConfigureModel(spec);
            result.modelSha256=Hash(spec.ModelPath);
            Texture2D color=BakeTexture(spec,spec.color,"basecolor",report,result);
            Texture2D normal=BakeTexture(spec,spec.normal,"normal",report,result);
            Texture2D smooth=spec.roughness==null?null:BakeTexture(spec,spec.roughness,"smoothness",report,result);
            Material material=MakeMaterial(spec,color,normal,smooth,report);
            AnimationClip source=SelectClip(spec);result.sourceClip=source.name;result.clipLength=source.length;
            GameObject model=AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            Require(model!=null,"Imported native FBX missing.");
            GameObject instance=(GameObject)PrefabUtility.InstantiatePrefab(model);GameObject wrapper=null;
            try {
                Animator animator=instance.GetComponent<Animator>();Require(animator!=null&&animator.avatar!=null&&animator.avatar.isValid&&!animator.avatar.isHuman,"Valid Generic avatar required.");
                animator.enabled=false;animator.applyRootMotion=false;
                AnimationClip loop=MakeLoop(spec,source,instance,report,result);
                InspectAnimation(spec,instance,loop,result);Save(report);
                string controllerPath=spec.BakedRoot+"/"+spec.id+"-swim.controller";Plan(controllerPath,report);
                AnimatorController controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                AnimatorState state=controller.layers[0].stateMachine.AddState("Native in-place Swim");state.motion=loop;controller.layers[0].stateMachine.defaultState=state;
                // Do not serialize a sampled endpoint as rest pose.
                Object.DestroyImmediate(instance);instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
                animator=instance.GetComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.runtimeAnimatorController=controller;
                foreach(Renderer renderer in instance.GetComponentsInChildren<Renderer>(true)) {
                    Require(renderer is SkinnedMeshRenderer,"Unexpected rigid renderer.");var materials=renderer.sharedMaterials;Require(materials.Length>0,"Missing material slots.");
                    for(int m=0;m<materials.Length;m++)materials[m]=material;renderer.sharedMaterials=materials;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                    ((SkinnedMeshRenderer)renderer).updateWhenOffscreen=false;
                }
                wrapper=new GameObject(spec.id+" (native animated)");var correction=new GameObject("Static anatomical orientation and measured scale");correction.transform.SetParent(wrapper.transform,false);
                Quaternion heading=Quaternion.Inverse(Quaternion.LookRotation(result.sourceNose,result.sourceUp));float scale=spec.targetDiagonal/result.sourceDiagonal;
                correction.transform.localRotation=heading;correction.transform.localScale=Vector3.one*scale;
                correction.transform.localPosition=-(heading*((result.sourceMin+result.sourceMax)*.5f))*scale;result.headingEuler=heading.eulerAngles;
                instance.transform.SetParent(correction.transform,false);
                Matrix4x4 navigation=Matrix4x4.TRS(correction.transform.localPosition,heading,Vector3.one*scale);
                Bounds envelope=default;bool first=true;
                foreach(PoseRecord pose in result.poses) {Bounds b=OceanAuthoredFishGeometry.TransformBounds(new Bounds((pose.min+pose.max)*.5f,pose.max-pose.min),navigation);if(first){envelope=b;first=false;}else envelope.Encapsulate(b);}
                result.navigationMin=envelope.min;result.navigationMax=envelope.max;
                Bounds initial=OceanAuthoredFishGeometry.TransformBounds(new Bounds((result.sourceMin+result.sourceMax)*.5f,result.sourceMax-result.sourceMin),navigation);
                result.actualLength=initial.size.z;
                var geometry=new OceanAuthoredFishGeometry {sourceMeshSha256=result.modelSha256,firstPoseDiagonal=spec.targetDiagonal,navigationCenter=envelope.center,navigationSize=envelope.size*1.04f};
                Require(geometry.IsValid,"Measured native navigation geometry invalid.");
                Plan(spec.GeometryPath,report);File.WriteAllText(spec.GeometryPath,JsonUtility.ToJson(geometry,true));AssetDatabase.ImportAsset(spec.GeometryPath,ImportAssetOptions.ForceSynchronousImport);
                Plan(spec.PrefabPath,report);Require(PrefabUtility.SaveAsPrefabAsset(wrapper,spec.PrefabPath)!=null,"Native prefab save failed.");
                RenderPoseEvidence(spec,wrapper,loop,instance,material,result,report.directory);
                Debug.Log("MARINE_COMPANION_MEASURED id="+spec.id+" vertices="+result.vertices+" triangles="+result.triangles+" bones="+result.bones+" clip="+loop.name+" length="+loop.length+" bodyLengthM="+result.actualLength+" bodyDiagonalM="+spec.targetDiagonal+" textureBytes="+result.runtimeTextureBytes);
                Save(report);
            } finally {if(wrapper!=null)Object.DestroyImmediate(wrapper);else if(instance!=null)Object.DestroyImmediate(instance);}
        }

        static void ConfigureModel(Spec spec)
        {
            var importer=AssetImporter.GetAtPath(spec.ModelPath) as ModelImporter;Require(importer!=null,"Missing ModelImporter.");
            importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=true;importer.animationType=ModelImporterAnimationType.Generic;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.resampleCurves=true;
            importer.optimizeGameObjects=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;importer.importBlendShapes=true;
            importer.isReadable=true;importer.meshCompression=ModelImporterMeshCompression.Off;importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.CalculateMikk;
            importer.useFileScale=true;importer.globalScale=1f;importer.skinWeights=ModelImporterSkinWeights.Custom;importer.maxBonesPerVertex=32;importer.minBoneWeight=0f;
            importer.SaveAndReimport();
        }
        static AnimationClip SelectClip(Spec spec)
        {
            AnimationClip[] clips=AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
            Require(clips.Length==1,"Expected exactly one authored native animation; selection must not silently change: "+spec.id);
            Require(!clips[0].legacy&&clips[0].length>.5f&&clips[0].length<30f,"Unexpected authored duration/type.");return clips[0];
        }
        static AnimationClip MakeLoop(Spec spec,AnimationClip original,GameObject instance,Report report,AnimalReport result)
        {
            AnimationClip clip=Object.Instantiate(original);clip.name=spec.id+" native in-place Swim";clip.wrapMode=WrapMode.Loop;
            try {
                Require(spec.loopStart>=0f&&spec.loopDuration>1f&&spec.loopStart+spec.loopDuration<=original.length+.0001f,"Reviewed source cycle outside original clip.");
                Require(AnimationUtility.GetObjectReferenceCurveBindings(original).Length==0&&AnimationUtility.GetAnimationEvents(original).Length==0,"Unexpected event/object-reference animation.");
                result.sourceCycleStart=spec.loopStart;result.sourceCycleDuration=spec.loopDuration;
                foreach(EditorCurveBinding binding in AnimationUtility.GetCurveBindings(original)) {
                    AnimationCurve curve=AnimationUtility.GetEditorCurve(original,binding);Require(curve!=null&&curve.length>0,"Empty original native curve.");
                    AnimationUtility.SetEditorCurve(clip,binding,CropCurve(curve,spec.loopStart,spec.loopDuration));
                }
                if(spec.fixedBone!=null) {
                    EditorCurveBinding[] matches=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Transform)&&b.propertyName==spec.fixedAxis&&Path.GetFileName(b.path)==spec.fixedBone).ToArray();
                    Require(matches.Length==1,"Must explicitly identify only the reviewed non-root turtle locomotion track.");
                    EditorCurveBinding binding=matches[0];Transform bone=instance.transform.Find(binding.path);
                    Require(bone!=null&&bone!=instance.transform,"Turtle locomotion binding must be an actual non-root body bone.");
                    AnimationCurve curve=AnimationUtility.GetEditorCurve(original,binding);Require(curve!=null&&curve.length>2,"Missing native locomotion samples.");
                    float start=curve.Evaluate(spec.loopStart),end=curve.Evaluate(original.length);float range=curve.keys.Max(x=>x.value)-curve.keys.Min(x=>x.value);
                    Debug.Log("MARINE_COMPANION_BODY_TRACK path="+binding.path+" start="+start+" end="+end+" range="+range);
                    Require(Finite(range)&&range>.0001f&&Mathf.Abs(end-start)>range*.1f,"Reviewed turtle Bone.y has no substantial forward trajectory after import-unit conversion.");
                    result.positionEdits.Add(new PositionEdit{path=binding.path,property=binding.propertyName,originalStart=start,originalEnd=end,originalRange=range,originalKeys=curve.length,constant=start});
                    AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0f,spec.loopDuration,start));
                }
                AnimationClipSettings settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=false;settings.loopBlendOrientation=false;
                settings.loopBlendPositionY=false;settings.loopBlendPositionXZ=false;settings.startTime=0f;settings.stopTime=spec.loopDuration;AnimationUtility.SetAnimationClipSettings(clip,settings);
                Require(Mathf.Abs(clip.length-spec.loopDuration)<.001f,"Derived source cycle duration differs.");
                string path=spec.BakedRoot+"/"+spec.id+"-swim.anim";Plan(path,report);AssetDatabase.CreateAsset(clip,path);result.derivedClip=path;
                // All remaining bindings must evaluate identically to the retained authored cycle; no replacement swim poses.
                EditorCurveBinding[] before=AnimationUtility.GetCurveBindings(original),after=AnimationUtility.GetCurveBindings(clip);Require(before.Length==after.Length,"Native curve roster changed.");
                foreach(EditorCurveBinding binding in before) {
                    bool edited=result.positionEdits.Any(x=>x.path==binding.path&&x.property==binding.propertyName);AnimationCurve a=AnimationUtility.GetEditorCurve(original,binding),b=AnimationUtility.GetEditorCurve(clip,binding);
                    Require(b!=null,"Native curve removed.");if(edited)continue;
                    for(int k=0;k<=120;k++) {float time=spec.loopDuration*k/120f;Require(Mathf.Abs(a.Evaluate(time+spec.loopStart)-b.Evaluate(time))<.0002f,"Unreviewed native cycle pose change: "+binding.path+"/"+binding.propertyName); }
                }
                return clip;
            } catch {if(!AssetDatabase.Contains(clip))Object.DestroyImmediate(clip);throw;}
        }

        static AnimationCurve CropCurve(AnimationCurve source,float start,float duration)
        {
            float stop=start+duration;var keys=new List<Keyframe>();keys.Add(CurveBoundary(source,start,0f));
            foreach(Keyframe key in source.keys)if(key.time>start+.00001f&&key.time<stop-.00001f) {Keyframe shifted=key;shifted.time-=start;keys.Add(shifted);}
            keys.Add(CurveBoundary(source,stop,duration));return new AnimationCurve(keys.ToArray()){preWrapMode=source.preWrapMode,postWrapMode=source.postWrapMode};
        }
        static Keyframe CurveBoundary(AnimationCurve source,float sourceTime,float targetTime)
        {
            foreach(Keyframe key in source.keys)if(Mathf.Abs(key.time-sourceTime)<.00001f) {Keyframe shifted=key;shifted.time=targetTime;return shifted;}
            const float step=.0001f;float value=source.Evaluate(sourceTime),derivative=(source.Evaluate(sourceTime+step)-source.Evaluate(sourceTime-step))/(2f*step);
            return new Keyframe(targetTime,value,derivative,derivative);
        }

        static void InspectAnimation(Spec spec,GameObject instance,AnimationClip clip,AnimalReport result)
        {
            var skins=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);Require(skins.Length==1,"Expected exactly one native skin.");var skin=skins[0];Mesh source=skin.sharedMesh;
            Require(source!=null&&source.isReadable&&source.vertexCount>100&&skin.bones.Length>=10&&source.bindposes.Length==skin.bones.Length&&skin.rootBone!=null,"Native skin/bones/bindposes incomplete.");
            Require(source.uv.Length==source.vertexCount&&source.normals.Length==source.vertexCount&&source.tangents.Length==source.vertexCount,"Original UV, normals and tangents required.");
            result.vertices=source.vertexCount;result.bones=skin.bones.Length;result.bindposes=source.bindposes.Length;result.skinLocalScale=skin.transform.localScale;result.skinLossyScale=skin.transform.lossyScale;
            for(int s=0;s<source.subMeshCount;s++){Require(source.GetTopology(s)==MeshTopology.Triangles,"Nontriangular skin.");result.triangles+=(long)source.GetIndexCount(s)/3;}
            Require(result.triangles>500&&result.triangles<12000,"Unexpected actual imported geometry budget.");
            Mesh baked=new Mesh();
            try {
                clip.SampleAnimation(instance,0f);Vector3[] first=WorldVertices(skin,baked);Bounds firstBounds=BoundsOf(first);float diagonal=firstBounds.size.magnitude;
                result.sourceDiagonal=diagonal;result.sourceMin=firstBounds.min;result.sourceMax=firstBounds.max;
                Require(diagonal>.005f&&diagonal<1000f,"Implausible source pose scale.");
                Transform head=FindUnique(instance.transform,spec.head),tail=FindUnique(instance.transform,spec.tail);
                Vector3 tailPoint=tail.position;if(spec.tailOther!=null)tailPoint=(tailPoint+FindUnique(instance.transform,spec.tailOther).position)*.5f;
                Vector3 nose=(head.position-tailPoint).normalized;
                // Blender-to-Unity importer supplies world Y up; anatomical render evidence is required before scene use.
                Vector3 up=Vector3.ProjectOnPlane(Vector3.up,nose).normalized;
                Require(nose.sqrMagnitude>.9f&&up.sqrMagnitude>.9f&&Mathf.Abs(Vector3.Dot(nose,up))<.001f,"Cannot establish explicit head/tail basis.");
                result.sourceNose=nose;result.sourceUp=up;result.headingPolicy="Measured rig head="+spec.head+" tail="+spec.tail+(spec.tailOther==null?"":"/"+spec.tailOther)+"; projected imported world +Y dorsal. Static +Z nose/+Y up wrapper. CPU source stills require anatomical review.";
                Quaternion[] rotations=skin.bones.Select(b=>b.localRotation).ToArray();Vector3 headStart=head.position,rootPosition=instance.transform.position;Quaternion rootRotation=instance.transform.rotation;Vector3 rootScale=instance.transform.localScale;
                for(int b=0;b<skin.bones.Length;b++)result.boneRoster.Add(new BoneRecord{path=AnimationUtility.CalculateTransformPath(skin.bones[b],instance.transform),firstPosition=skin.bones[b].position});
                result.sampleEnd=clip.length-Mathf.Min(.0001f,clip.length*.0001f);result.poseSamples=121;
                for(int sample=0;sample<=120;sample++) {
                    float time=result.sampleEnd*sample/120f;clip.SampleAnimation(instance,time);Vector3[] vertices=WorldVertices(skin,baked);Require(vertices.Length==first.Length,"Skin topology changed across poses.");
                    Bounds bounds=BoundsOf(vertices);result.poses.Add(new PoseRecord{time=time,min=bounds.min,max=bounds.max});
                    result.maxHeadTravel=Mathf.Max(result.maxHeadTravel,Vector3.Distance(head.position,headStart));result.maxCenterTravel=Mathf.Max(result.maxCenterTravel,Vector3.Distance(firstBounds.center,bounds.center));
                    result.maxPoseDiagonal=Mathf.Max(result.maxPoseDiagonal,bounds.size.magnitude);result.rootTravel=Mathf.Max(result.rootTravel,Vector3.Distance(rootPosition,instance.transform.position));
                    Require(Quaternion.Angle(rootRotation,instance.transform.rotation)<.02f&&(rootScale-instance.transform.localScale).magnitude<.0001f,"Animation rotates/scales navigation root.");
                    for(int v=0;v<vertices.Length;v++){float travel=Vector3.Distance(vertices[v],first[v]);result.maxVertexTravel=Mathf.Max(result.maxVertexTravel,travel);if(sample==120)result.loopVertexGap=Mathf.Max(result.loopVertexGap,travel);}
                    foreach(Transform bone in skin.bones)Require(bone!=null&&Finite(bone.localPosition)&&Finite(bone.localRotation),"Nonfinite native bone.");
                }
                for(int b=0;b<skin.bones.Length;b++){float gap=Quaternion.Angle(rotations[b],skin.bones[b].localRotation);result.boneRoster[b].loopAngle=gap;result.loopBoneAngle=Mathf.Max(result.loopBoneAngle,gap);}
                // Source-specific motion is measured, not replaced with a fish shader. These broad safety gates
                // reject whole-body travel, explosions and loop pops; rendered anatomy remains a separate gate.
                Debug.Log("MARINE_COMPANION_POSES id="+spec.id+" sourceDiag="+diagonal+" vertexTravel="+result.maxVertexTravel+" headTravel="+result.maxHeadTravel+" centerTravel="+result.maxCenterTravel+" loopVertex="+result.loopVertexGap+" loopAngle="+result.loopBoneAngle);
                Require(result.rootTravel<diagonal*.001f&&result.maxHeadTravel<diagonal*.25f&&result.maxCenterTravel<diagonal*.35f,"Native swim still contains unsafe locomotion.");
                Require(result.maxVertexTravel>diagonal*.003f&&result.maxVertexTravel<diagonal*.9f&&result.maxPoseDiagonal<diagonal*1.5f,"Missing animation or native skin explosion.");
                Require(result.loopVertexGap<diagonal*.035f&&result.loopBoneAngle<5f,"Original animation endpoints are not seamless; retain evidence and review a genuine source cycle before admission.");
            } finally {Object.DestroyImmediate(baked);}
        }

        static Texture2D BakeTexture(Spec spec,string name,string role,Report report,AnimalReport animal)
        {
            bool srgb=role=="basecolor";string source=spec.SourceRoot+"/"+name,path=spec.BakedRoot+"/"+spec.id+"-"+role+"-astc.asset";
            var decoded=new Texture2D(2,2,TextureFormat.RGBA32,false,!srgb);Texture2D output=null;
            try {
                Require(ImageConversion.LoadImage(decoded,File.ReadAllBytes(source),false),"Canonical PNG decode failed.");
                Require(decoded.width==spec.resolution&&decoded.height==spec.resolution,"Original source resolution changed; no silent resize permitted.");
                Color32[] pixels=decoded.GetPixels32();Require(pixels.Length==spec.resolution*spec.resolution,"Decoded source pixel count differs.");
                for(int p=0;p<pixels.Length;p++) {Color32 c=pixels[p];pixels[p]=role=="normal"?new Color32(255,c.g,0,c.r):role=="smoothness"?new Color32(0,0,0,(byte)(255-c.r)):new Color32(c.r,c.g,c.b,255);}
                output=new Texture2D(spec.resolution,spec.resolution,TextureFormat.RGBA32,true,!srgb) {name=spec.id+" original "+role+" ASTC4x4",filterMode=FilterMode.Trilinear,anisoLevel=16,wrapMode=TextureWrapMode.Clamp};
                output.SetPixels32(pixels);output.Apply(true,false);int mips=1+(int)Mathf.Log(spec.resolution,2f);
                Require(output.mipmapCount==mips,"Full source-resolution mip chain required.");
                var record=new TextureRecord{path=path,role=role,sourceSha256=Hash(source),width=output.width,height=output.height,mips=mips,srgb=srgb};
                byte[][] before=new byte[mips][];long expectedBytes=0;
                for(int m=0;m<mips;m++) {before[m]=output.GetPixelData<byte>(m).ToArray();record.copyError=Math.Max(record.copyError,MaxError(before[m],ReadPixels(output,m,srgb)));int size=Math.Max(1,spec.resolution>>m);expectedBytes+=(long)((size+3)/4)*((size+3)/4)*16;}
                Require(record.copyError<=1,"Source RGBA/linear/sRGB/alpha/mip copy calibration failed.");
                EditorUtility.CompressTexture(output,TextureFormat.ASTC_4x4,TextureCompressionQuality.Best);output.Apply(false,false);
                record.runtimeBytes=output.GetRawTextureData<byte>().Length;record.format=output.format.ToString();record.graphicsFormat=output.graphicsFormat.ToString();
                Require(output.format==TextureFormat.ASTC_4x4&&output.mipmapCount==mips&&record.runtimeBytes==expectedBytes&&GraphicsFormatUtility.IsSRGBFormat(output.graphicsFormat)==srgb,"Native ASTC byte budget/mips/color-space contract failed.");
                double abs=0,squared=0;long count=0;long[] angleHistogram=new long[1801];long normals=0;double angleSum=0;
                for(int m=0;m<mips;m++) {
                    byte[] after=ReadPixels(output,m,srgb);Require(after.Length==before[m].Length,"Decoded mip pixel count changed.");
                    for(int p=0;p<after.Length;p++) {int error=Math.Abs(after[p]-before[m][p]);record.maximumError=Math.Max(record.maximumError,error);abs+=error;squared+=error*error;count++;}
                    if(role=="normal"&&m==0)for(int p=0;p<after.Length;p+=4) {
                        Vector3 original=UnpackNormal(before[m][p+3],before[m][p+1]),actual=UnpackNormal(after[p+3],after[p+1]);
                        double angle=Vector3.Angle(original,actual);record.maxNormalAngle=Math.Max(record.maxNormalAngle,angle);angleSum+=angle;normals++;
                        angleHistogram[Math.Min(1800,(int)(angle*10))]++;
                    }
                }
                record.meanByteError=abs/count;record.rmsByteError=Math.Sqrt(squared/count);
                if(role=="normal") {
                    record.meanNormalAngle=angleSum/normals;long seen=0;for(int b=0;b<angleHistogram.Length;b++){seen+=angleHistogram[b];if(seen>=normals*.95){record.p95NormalAngle=(b+1)/10.0;break;}}
                    Require(record.meanNormalAngle<=2.5&&record.p95NormalAngle<=8&&record.maxNormalAngle<=45,"4x4 native normal quality insufficient; do not silently reduce source resolution: mean="+record.meanNormalAngle+" p95="+record.p95NormalAngle+" max="+record.maxNormalAngle);
                } else Require(record.meanByteError<=8&&record.rmsByteError<=12,"Native color/roughness ASTC error exceeds quality budget.");
                Plan(path,report);output.Apply(false,true);AssetDatabase.CreateAsset(output,path);animal.textures.Add(record);animal.runtimeTextureBytes+=record.runtimeBytes;Save(report);
                return output;
            } finally {Object.DestroyImmediate(decoded);if(output!=null&&!AssetDatabase.Contains(output))Object.DestroyImmediate(output);}
        }
        static Vector3 UnpackNormal(byte x,byte y) {float nx=x/127.5f-1f,ny=y/127.5f-1f;return new Vector3(nx,ny,Mathf.Sqrt(Mathf.Max(0f,1f-nx*nx-ny*ny))).normalized;}
        static Material MakeMaterial(Spec spec,Texture2D color,Texture2D normal,Texture2D smoothness,Report report)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");Require(shader!=null&&shader.isSupported,"URP Lit required.");
            var material=new Material(shader){name=spec.id+" original native PBR"};material.SetTexture("_BaseMap",color);material.SetColor("_BaseColor",Color.white);
            material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",1f);material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic",0f);material.SetFloat("_Smoothness",smoothness==null?.40f:1f);
            if(smoothness!=null){material.SetTexture("_MetallicGlossMap",smoothness);material.EnableKeyword("_METALLICSPECGLOSSMAP");}
            material.SetFloat("_SmoothnessTextureChannel",0);material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_Cull",(float)CullMode.Back);material.SetFloat("_ZWrite",1);
            material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.SetFloat("_QueueControl",1);material.renderQueue=(int)RenderQueue.Geometry;
            string path=spec.BakedRoot+"/"+spec.id+"-urp.mat";Plan(path,report);AssetDatabase.CreateAsset(material,path);return material;
        }

        /// <summary>Read-only pose rendering from retained partial/successful owned assets. Never admits a failed bake.</summary>
        public static void DiagnosticRetained()
        {
            try {
                Require(pending==null&&!EditorApplication.isPlayingOrWillChangePlaymode&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"Idle graphics Editor required.");
                Report report=ReadReport();VerifyFiles(report.after);foreach(Spec spec in Species)VerifySource(spec);
                string directory=Path.Combine(EvidenceRoot,"pose-diagnostic-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));Directory.CreateDirectory(directory);
                foreach(AnimalReport animal in report.animals) {
                    Spec spec=Species.Single(x=>x.id==animal.id);var model=AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(animal.derivedClip);var material=AssetDatabase.LoadAssetAtPath<Material>(spec.BakedRoot+"/"+spec.id+"-urp.mat");
                    if(model==null||clip==null||material==null||animal.sourceDiagonal<=0)continue;
                    var wrapper=new GameObject("Read-only native pose diagnostic");
                    try {
                        var correction=new GameObject("Measured orientation");correction.transform.SetParent(wrapper.transform,false);Quaternion heading=Quaternion.Inverse(Quaternion.LookRotation(animal.sourceNose,animal.sourceUp));float scale=spec.targetDiagonal/animal.sourceDiagonal;
                        correction.transform.localRotation=heading;correction.transform.localScale=Vector3.one*scale;correction.transform.localPosition=-(heading*((animal.sourceMin+animal.sourceMax)*.5f))*scale;
                        GameObject instance=(GameObject)PrefabUtility.InstantiatePrefab(model);instance.transform.SetParent(correction.transform,false);
                        RenderPoseEvidence(spec,wrapper,clip,instance,material,animal,directory);
                    } finally {Object.DestroyImmediate(wrapper);}
                }
                Debug.Log("MARINE_COMPANION_DIAGNOSTIC_COMPLETE "+directory);Exit(0);
            } catch(Exception ex){Debug.LogException(ex);Exit(1);if(!Application.isBatchMode)throw;}
        }
        static void RenderPoseEvidence(Spec spec,GameObject wrapper,AnimationClip clip,GameObject instance,Material material,AnimalReport result,string directory)
        {
            Scene scene=EditorSceneManager.NewPreviewScene();RenderTexture target=null;Texture2D pixels=null;var meshes=new List<Mesh>();var objects=new List<GameObject>();RenderTexture previous=RenderTexture.active;
            try {
                SceneManager.MoveGameObjectToScene(wrapper,scene);foreach(Animator a in instance.GetComponentsInChildren<Animator>(true))a.enabled=false;
                SkinnedMeshRenderer skin=instance.GetComponentInChildren<SkinnedMeshRenderer>(true);skin.updateWhenOffscreen=true;
                var cameraHost=new GameObject("Native pose evidence camera");SceneManager.MoveGameObjectToScene(cameraHost,scene);Camera camera=cameraHost.AddComponent<Camera>();
                camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.aspect=16f/9f;camera.nearClipPlane=.001f;camera.farClipPlane=30;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.025f,.16f,.20f);camera.stereoTargetEye=StereoTargetEyeMask.None;var data=camera.GetUniversalAdditionalCameraData();data.allowXRRendering=false;data.renderPostProcessing=false;
                for(int i=0;i<2;i++){var host=new GameObject("Evidence light");SceneManager.MoveGameObjectToScene(host,scene);Light light=host.AddComponent<Light>();light.type=LightType.Directional;light.intensity=i==0?1.7f:.75f;light.shadows=LightShadows.None;light.transform.rotation=Quaternion.Euler(i==0?new Vector3(35,-40,0):new Vector3(-25,140,0));}
                float[] times={0f,clip.length*.25f,clip.length*.5f,clip.length*.75f,clip.length-.0001f};Bounds union=default;bool have=false;
                foreach(float time in times) {
                    clip.SampleAnimation(instance,time);var snapshot=new Mesh{name="CPU native pose "+time};Vector3[] vertices=WorldVertices(skin,snapshot);snapshot.vertices=vertices;
                    Quaternion rotation=skin.transform.rotation;Vector3[] normals=snapshot.normals;for(int n=0;n<normals.Length;n++)normals[n]=rotation*normals[n];snapshot.normals=normals;
                    Vector4[] tangents=snapshot.tangents;float handedness=skin.transform.localToWorldMatrix.determinant<0?-1:1;
                    for(int n=0;n<tangents.Length;n++){Vector3 t=rotation*new Vector3(tangents[n].x,tangents[n].y,tangents[n].z);tangents[n]=new Vector4(t.x,t.y,t.z,tangents[n].w*handedness);}snapshot.tangents=tangents;snapshot.RecalculateBounds();
                    meshes.Add(snapshot);if(!have){union=snapshot.bounds;have=true;}else union.Encapsulate(snapshot.bounds);
                    var host=new GameObject("CPU native skinned snapshot");SceneManager.MoveGameObjectToScene(host,scene);host.AddComponent<MeshFilter>().sharedMesh=snapshot;host.AddComponent<MeshRenderer>().sharedMaterial=material;host.SetActive(false);objects.Add(host);
                }
                skin.enabled=false;target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
                string folder=Path.Combine(directory,spec.id+"-cpu-native-poses");Directory.CreateDirectory(folder);
                Vector3[] views={new Vector3(1,.35f,.8f),new Vector3(1,0,0),new Vector3(0,1f,.01f)};
                for(int view=0;view<views.Length;view++) {
                    camera.transform.position=union.center+views[view].normalized*Mathf.Max(2,union.size.magnitude*3);camera.transform.LookAt(union.center,Vector3.up);float fit=0;
                    for(int c=0;c<8;c++){Vector3 p=Vector3.Scale(union.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1));fit=Mathf.Max(fit,Mathf.Abs(Vector3.Dot(p,camera.transform.up)),Mathf.Abs(Vector3.Dot(p,camera.transform.right))/camera.aspect);}
                    camera.orthographicSize=Mathf.Max(.01f,fit*1.15f);
                    for(int frame=0;frame<objects.Count;frame++) {
                        objects[frame].SetActive(true);RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                        pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply(false,false);File.WriteAllBytes(Path.Combine(folder,"view-"+view+"-phase-"+frame+".png"),pixels.EncodeToPNG());objects[frame].SetActive(false);
                    }
                }
                File.WriteAllText(Path.Combine(folder,"metrics.json"),JsonUtility.ToJson(result,true));
            } finally {
                RenderTexture.active=previous;
                // Keep caller ownership intact; closing the preview scene otherwise destroys the live temporary wrapper.
                if(wrapper!=null&&wrapper.scene==scene)SceneManager.MoveGameObjectToScene(wrapper,SceneManager.GetActiveScene());
                EditorSceneManager.ClosePreviewScene(scene);foreach(Mesh mesh in meshes)if(mesh!=null)Object.DestroyImmediate(mesh);
                if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            }
        }

        /// <summary>Verify the published projection and every owned output, without re-admitting omitted download archives.
        /// Retains the original measured model, texture, pose and animation gates; performs no new sampling or writes.</summary>
        public static void ValidatePublishedAssets()
        {
            Require(pending == null, "Admission is still finalizing.");
            Require(File.Exists(Portable) && Hash(Portable) == "45ff7c2823f3789a922f7a0281348911cc6a06f8cca46411cacf5fce68c1cb29",
                "Missing or changed published admission projection.");
            Report report = JsonUtility.FromJson<Report>(File.ReadAllText(Portable));
            Require(report != null && report.publicationProjection &&
                report.sourceReportSha256 == "eadd3662595adbcb62a871a026a430a19b812eb2d47192d632f0074b9cae90dd",
                "Receipt must identify the exact original admission as a published projection.");
            Require(report.tool == "OceanMarineCompanionBake", "Published receipt tool differs.");
            Require(report.success && report.state == "admitted" && report.after != null && report.after.Count > 0,
                "Published projection is not a completed admission.");
            VerifyFiles(report.after);
            Require(Same(report.after, Snapshot(OwnedFiles())), "Published owned output roster changed.");
            ValidateOutputs(report);
            Debug.Log("OceanMarineCompanionBake PUBLISHED_ASSETS_PASS: sealed original output bytes and model/texture/animation gates; source download archives and headset acceptance are not revalidated.");
        }

        public static void ValidateForBuild()
        {
            Require(pending==null,"Native admission is still finalizing.");Report report=ReadReport();Require(report.success&&report.state=="admitted","No completed companions admission.");
            foreach(Spec spec in Species)VerifySource(spec);VerifyFiles(report.after);Require(Same(report.after,Snapshot(OwnedFiles())),"Owned resource roster changed.");ValidateOutputs(report);
            Debug.Log("MARINE_COMPANIONS_BUILD_ADMISSION_PASSED native turtle/dolphin; headset acceptance remains separate.");
        }
        static void ValidateOutputs(Report report)
        {
            Require(report.animals.Count==Species.Length&&File.Exists(Credits),"Incomplete resource admission.");
            foreach(Spec spec in Species) {
                AnimalReport animal=report.animals.Single(x=>x.id==spec.id);Require(animal.modelSha256==Hash(spec.ModelPath)&&animal.poseSamples==121&&animal.poses.Count==121,"Native pose/source receipt changed.");
                GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);Require(prefab!=null&&prefab.transform.localScale==Vector3.one&&prefab.transform.localRotation==Quaternion.identity,"Missing/altered unit navigation root.");
                Animator[] animators=prefab.GetComponentsInChildren<Animator>(true);Require(animators.Length==1&&animators[0].enabled&&!animators[0].applyRootMotion&&animators[0].avatar!=null&&animators[0].avatar.isValid&&!animators[0].avatar.isHuman,"Native Animator/avatar missing.");
                var controller=animators[0].runtimeAnimatorController as AnimatorController;AnimationClip clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(animal.derivedClip);
                Require(controller!=null&&controller.layers.Length==1&&controller.layers[0].stateMachine.states.Length==1&&controller.animationClips.Length==1&&controller.animationClips[0]==clip&&clip.isLooping,"Exactly one actual native Swim loop required.");
                Require(!prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(),"No procedural fish deformation or game components permitted on source prefab.");
                var skins=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);Require(skins.Length==1&&skins[0].sharedMesh.vertexCount==animal.vertices&&skins[0].bones.Length==animal.bones,"Native skin/bones changed.");
                foreach(Material m in skins[0].sharedMaterials)Require(m!=null&&m.shader.name=="Universal Render Pipeline/Lit"&&m.IsKeywordEnabled("_NORMALMAP")&&m.GetTexture("_BaseMap")!=null&&m.GetTexture("_BumpMap")!=null&&m.GetFloat("_Surface")==0&&m.GetFloat("_AlphaClip")==0&&m.GetFloat("_SmoothnessTextureChannel")==0&&!m.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"),"Native opaque URP PBR material invalid.");
                OceanAuthoredFishGeometry geometry=JsonUtility.FromJson<OceanAuthoredFishGeometry>(File.ReadAllText(spec.GeometryPath));Require(geometry!=null&&geometry.IsValid&&geometry.sourceMeshSha256==animal.modelSha256&&Mathf.Abs(geometry.firstPoseDiagonal-spec.targetDiagonal)<.00001f,"Measured geometry metadata invalid.");
                Require(animal.textures.Count==(spec.roughness==null?2:3),"Source map role roster changed.");
                foreach(TextureRecord record in animal.textures) {
                    Texture2D texture=AssetDatabase.LoadAssetAtPath<Texture2D>(record.path);
                    Require(texture!=null&&texture.format==TextureFormat.ASTC_4x4&&texture.width==spec.resolution&&texture.height==spec.resolution&&texture.mipmapCount==record.mips&&!texture.isReadable&&texture.filterMode==FilterMode.Trilinear&&texture.anisoLevel==16&&GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)==record.srgb,"Native source-resolution texture quality contract changed.");
                    Require(record.copyError<=1&&record.runtimeBytes>0&&(record.role=="normal"?record.meanNormalAngle<=2.5&&record.p95NormalAngle<=8&&record.maxNormalAngle<=45:record.meanByteError<=8&&record.rmsByteError<=12),"Native texture decode receipt failed.");
                }
                string[] dependencies=AssetDatabase.GetDependencies(spec.PrefabPath,true);Require(dependencies.Contains(spec.ModelPath)&&!dependencies.Any(x=>x.StartsWith(spec.SourceRoot+"/",StringComparison.Ordinal)&&x.EndsWith(".png",StringComparison.OrdinalIgnoreCase)),"Runtime prefab must retain native rig but not original uncompressed PNGs.");
                Require(animal.positionEdits.Count==(spec.fixedBone==null?0:1),"In-place derivation provenance changed.");
                if(spec.fixedBone!=null)foreach(PositionEdit edit in animal.positionEdits) {
                    EditorCurveBinding binding=AnimationUtility.GetCurveBindings(clip).Single(x=>x.type==typeof(Transform)&&x.path==edit.path&&x.propertyName==edit.property);AnimationCurve curve=AnimationUtility.GetEditorCurve(clip,binding);
                    Require(curve!=null&&curve.keys.All(x=>Mathf.Abs(x.value-edit.constant)<.000001f),"Non-root turtle travel was restored; applyRootMotion=false alone is insufficient.");
                }
            }
        }

        static void VerifySource(Spec spec)
        {
            string archivePath=spec.package+"/source-original.zip";Require(File.Exists(archivePath)&&new FileInfo(archivePath).Length==spec.archiveBytes&&Hash(archivePath)==spec.archiveHash,"Original archive identity changed: "+spec.id);
            Receipt receipt=JsonUtility.FromJson<Receipt>(File.ReadAllText(spec.package+"/receipt.json"));
            Require(receipt!=null&&receipt.uid==spec.uid&&receipt.source_url==spec.url&&receipt.author==spec.author&&receipt.license=="CC-BY-4.0"&&receipt.license_url=="https://creativecommons.org/licenses/by/4.0/"&&receipt.bytes==spec.archiveBytes&&receipt.sha256==spec.archiveHash,"Commercial CC BY provenance differs from reviewed source: "+spec.id);
            string metadataHash=spec.id=="sea-turtle"?"977d7e8ea0d5c8b30dfa875dfd4cb82c9163ad720f8f781442c0ea49a043afc0":"0914f3eda29af12c7fdc0852a21af28d2975a333f3d6bb60a2e423a8526d7d73";
            Require(receipt.license_metadata_sha256==metadataHash&&receipt.license_metadata_file=="Sources/MarineExpansion/candidates-20261017/"+spec.uid+".metadata.json"&&Hash(receipt.license_metadata_file)==metadataHash,"Public license metadata changed.");
            foreach(Input input in spec.inputs)Require(File.Exists(spec.package+"/"+input.path)&&new FileInfo(spec.package+"/"+input.path).Length==input.bytes&&Hash(spec.package+"/"+input.path)==input.hash,"Extracted original bytes differ: "+input.path);
            using(var archive=ZipFile.OpenRead(archivePath)) {
                VerifyRoster(archive,receipt.roster);
                foreach(Input input in spec.inputs) {
                    string entry=input.path.Replace("extracted/","");if(entry.StartsWith("nested/",StringComparison.Ordinal))continue;
                    Require(ArchiveHash(archive.GetEntry(entry))==input.hash,"Original archive and extracted map/FBX differ.");
                }
                if(spec.id=="dolphin") {
                    Require(receipt.nested_archive!=null&&receipt.nested_archive.path=="source/DolphinanimTest_01.zip","Unexpected nested original archive.");
                    ZipArchiveEntry nestedEntry=archive.GetEntry(receipt.nested_archive.path);Require(nestedEntry!=null,"Nested FBX archive missing.");
                    using(var buffer=new MemoryStream()) {
                        using(Stream stream=nestedEntry.Open())stream.CopyTo(buffer);buffer.Position=0;
                        using(var nested=new ZipArchive(buffer,ZipArchiveMode.Read,true)) {
                            VerifyRoster(nested,receipt.nested_archive.roster);
                            Require(ArchiveHash(nested.GetEntry(spec.model))==spec.inputs.Single(x=>x.path.EndsWith("/"+spec.model,StringComparison.Ordinal)).hash,"Nested original FBX differs.");
                        }
                    }
                }
            }
        }
        static void VerifyRoster(ZipArchive archive,Roster[] roster)
        {
            Require(roster!=null&&archive.Entries.Count==roster.Length&&roster.Select(x=>x.path).Distinct(StringComparer.Ordinal).Count()==roster.Length,"Original archive roster is not closed/unique.");
            foreach(ZipArchiveEntry entry in archive.Entries) {
                Require(!Path.IsPathRooted(entry.FullName)&&!entry.FullName.Contains("..")&&!entry.FullName.Contains("\\"),"Unsafe archive path.");
                Roster row=roster.SingleOrDefault(x=>x.path==entry.FullName);Require(row!=null&&entry.Length==row.bytes,"Archive roster changed.");
                uint crc=0xffffffff;using(Stream stream=entry.Open()){byte[] bytes=new byte[65536];int count;while((count=stream.Read(bytes,0,bytes.Length))>0)for(int i=0;i<count;i++)crc=CrcTable[(crc^bytes[i])&255]^(crc>>8);}
                Require((long)~crc==row.crc,"Original ZIP CRC failed: "+entry.FullName);
            }
        }
        static string ArchiveHash(ZipArchiveEntry entry) {Require(entry!=null,"Original archive entry missing.");using(Stream stream=entry.Open())using(var sha=SHA256.Create())return Hex(sha.ComputeHash(stream));}
        static readonly uint[] CrcTable=MakeCrcTable();
        static uint[] MakeCrcTable() {var table=new uint[256];for(uint n=0;n<256;n++){uint c=n;for(int k=0;k<8;k++)c=(c>>1)^((c&1)!=0?0xedb88320u:0);table[n]=c;}return table;}

        static void CalibrateReadback()
        {
            if(calibrated)return;Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(DecoderPath);Require(shader!=null&&shader.isSupported,"Existing exact-mip decoder required.");decoder=new Material(shader){hideFlags=HideFlags.HideAndDontSave};bool orientation=false;
            foreach(bool srgb in new[]{false,true}) {
                var known=new Texture2D(8,8,TextureFormat.RGBA32,4,!srgb);
                try {
                    byte[][] expected=new byte[4][];
                    for(int m=0;m<4;m++){int size=Math.Max(1,8>>m);byte[] bytes=new byte[size*size*4];for(int y=0;y<size;y++)for(int x=0;x<size;x++){int p=(y*size+x)*4;bytes[p]=(byte)((x*31+y*17+m*61)&255);bytes[p+1]=(byte)((x*13+y*37+m*29)&255);bytes[p+2]=(byte)((x*71+y*7+m*43)&255);bytes[p+3]=(byte)((x*19+y*29+m*47)&255);}expected[m]=bytes;known.SetPixelData(bytes,m);}
                    known.Apply(false,false);
                    for(int m=0;m<4;m++){int size=Math.Max(1,8>>m);byte[] actual=RenderDecoded(known,m,srgb);if(!orientation){flipRows=MaxError(expected[m],actual)>1;orientation=true;}if(flipRows)FlipRows(actual,size,size);Require(MaxError(expected[m],actual)<=1,"Independent exact RGBA/mip/alpha/sRGB decoder calibration failed.");}
                } finally {Object.DestroyImmediate(known);}
            }
            calibrated=true;
        }
        static byte[] ReadPixels(Texture texture,int mip,bool srgb)
        {Require(calibrated,"Decoder not independently calibrated.");byte[] bytes=RenderDecoded(texture,mip,srgb);if(flipRows)FlipRows(bytes,Math.Max(1,texture.width>>mip),Math.Max(1,texture.height>>mip));return bytes;}
        static byte[] RenderDecoded(Texture texture,int mip,bool srgb)
        {
            var descriptor=new RenderTextureDescriptor(Math.Max(1,texture.width>>mip),Math.Max(1,texture.height>>mip)){graphicsFormat=GraphicsFormat.R8G8B8A8_UNorm,depthBufferBits=0,msaaSamples=1,useMipMap=false,autoGenerateMips=false};
            RenderTexture target=RenderTexture.GetTemporary(descriptor),previous=RenderTexture.active;bool previousSrgb=GL.sRGBWrite;
            try {
                decoder.SetTexture("_Source",texture);decoder.SetInt("_Mip",mip);decoder.SetFloat("_EncodeSrgb",srgb?1:0);GL.sRGBWrite=false;Graphics.Blit(texture,target,decoder,0);
                var request=AsyncGPUReadback.Request(target,0,GraphicsFormat.R8G8B8A8_UNorm);request.WaitForCompletion();Require(!request.hasError,"Native ASTC GPU decode failed.");return request.GetData<byte>().ToArray();
            } finally {decoder.SetTexture("_Source",null);GL.sRGBWrite=previousSrgb;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        static void ReleaseReadback(){if(decoder!=null)Object.DestroyImmediate(decoder);decoder=null;calibrated=false;flipRows=false;}
        static int MaxError(byte[] a,byte[] b){Require(a.Length==b.Length,"Readback byte count changed.");int max=0;for(int i=0;i<a.Length;i++)max=Math.Max(max,Math.Abs(a[i]-b[i]));return max;}
        static void FlipRows(byte[] bytes,int width,int height){int stride=width*4;for(int y=0;y<height/2;y++)for(int x=0;x<stride;x++){int a=y*stride+x,b=(height-y-1)*stride+x;byte value=bytes[a];bytes[a]=bytes[b];bytes[b]=value;}}

        static void BeginFinalize(Report report,Exception error)
        {
            if(pending!=null){pending.error=error??pending.error;return;}report.success=false;report.state=error==null?"stabilizing":"stabilizing_failure";report.error=error?.ToString();report.after=Snapshot(OwnedFiles());Save(report);
            pending=new Pending{report=report,error=error,last=report.after};EditorApplication.update+=FinalizeUpdate;
        }
        static void FinalizeUpdate()
        {
            if(pending==null){EditorApplication.update-=FinalizeUpdate;return;}if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;Pending work=pending;
            try {
                AssetDatabase.SaveAssets();var now=Snapshot(OwnedFiles());work.ticks++;work.stable=Same(work.last,now)?work.stable+1:0;work.last=now;work.report.after=now;work.report.finalizationTicks=work.ticks;Save(work.report);
                Require(work.ticks<=30,"Deferred Unity asset writes did not stabilize.");if(work.ticks<3||work.stable<2)return;
                VerifyFiles(work.report.protectedFiles);VerifyFiles(work.report.sourceFiles);foreach(Spec spec in Species)VerifySource(spec);
                if(work.error==null)ValidateOutputs(work.report);work.report.success=work.error==null;work.report.state=work.error==null?"admitted":"failed";work.report.error=work.error?.ToString();Save(work.report);
                if(work.report.success)File.WriteAllText(Portable,JsonUtility.ToJson(work.report,true));
                Debug.Log(work.error==null?"MARINE_COMPANION_BAKE_PASSED "+Latest:"MARINE_COMPANION_BAKE_FAILED "+Latest);pending=null;EditorApplication.update-=FinalizeUpdate;ReleaseReadback();Exit(work.error==null?0:1);
            } catch(Exception ex) {
                work.report.success=false;work.report.state="failed";work.report.error=ex.ToString();work.report.after=Snapshot(OwnedFiles());Save(work.report);Debug.LogException(ex);pending=null;EditorApplication.update-=FinalizeUpdate;ReleaseReadback();Exit(1);
            }
        }
        [MenuItem("Swan/Codex/Marine Expansion/Roll back latest owned companions admission")]
        public static void Rollback()
        {
            try {
                Require(pending==null&&!EditorApplication.isPlayingOrWillChangePlaymode,"Idle Edit Mode required.");Report report=ReadReport();Require(report.state!="rolled_back","Already rolled back.");
                VerifyFiles(report.after);Require(Same(report.after,Snapshot(OwnedFiles())),"Unknown/edited owned outputs; refusing rollback.");VerifyFiles(report.protectedFiles);
                foreach(string path in Species.SelectMany(x=>new[]{x.PrefabPath,x.GeometryPath}).Concat(new[]{Sources,Baked,Credits}))if(File.Exists(path)||Directory.Exists(path))Require(AssetDatabase.DeleteAsset(path),"Cannot delete owned asset: "+path);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);Require(!OwnedFiles().Any(),"Owned resources remain.");report.success=false;report.state="rolled_back";Save(report);Exit(0);
            } catch(Exception ex){Debug.LogException(ex);Exit(1);if(!Application.isBatchMode)throw;}
        }
        static Report ReadReport()
        {
            string path=File.Exists(Latest)?Latest:Portable;Require(File.Exists(path),"No companions transaction receipt.");Report report=JsonUtility.FromJson<Report>(File.ReadAllText(path));
            Require(report!=null&&report.tool=="OceanMarineCompanionBake","Invalid ownership receipt.");if(path==Portable)Require(report.success&&report.state=="admitted","Portable receipt is not a completed admission.");return report;
        }
        static void Save(Report report){Directory.CreateDirectory(EvidenceRoot);Directory.CreateDirectory(report.directory);string json=JsonUtility.ToJson(report,true);File.WriteAllText(Latest,json);File.WriteAllText(Path.Combine(report.directory,"report.json"),json);}
        static void Plan(string path,Report report){Require(!File.Exists(path)&&!Directory.Exists(path),"Never overwrite a different asset: "+path);report.plannedPaths.Add(path);Save(report);}
        static void MakeFolder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');MakeFolder(parent);Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))),"Folder creation failed.");}
        static IEnumerable<string> OwnedFiles()
        {
            foreach(string root in new[]{Sources,Baked}){foreach(string path in FilesUnderOrSelf(root))yield return path;if(File.Exists(root+".meta"))yield return root+".meta";}
            foreach(string path in Species.SelectMany(x=>new[]{x.PrefabPath,x.GeometryPath}).Concat(new[]{Credits})) {if(File.Exists(path))yield return path;if(File.Exists(path+".meta"))yield return path+".meta";}
        }
        static IEnumerable<string> FilesUnderOrSelf(string path)=>File.Exists(path)?new[]{path}:Directory.Exists(path)?Directory.GetFiles(path,"*",SearchOption.AllDirectories).Select(x=>x.Replace('\\','/')).Where(x=>!Path.GetFileName(x).StartsWith(".",StringComparison.Ordinal)):Enumerable.Empty<string>();
        static List<FileRecord> Snapshot(IEnumerable<string> paths)=>paths.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).Select(x=>new FileRecord{path=x,sha256=Hash(x),bytes=new FileInfo(x).Length}).ToList();
        static bool Same(List<FileRecord> a,List<FileRecord> b)=>a.Count==b.Count&&a.Zip(b,(x,y)=>x.path==y.path&&x.sha256==y.sha256&&x.bytes==y.bytes).All(x=>x);
        static void VerifyFiles(List<FileRecord> files){foreach(var file in files)Require(File.Exists(file.path)&&new FileInfo(file.path).Length==file.bytes&&Hash(file.path)==file.sha256,"Source/protected/owned hash changed: "+file.path);}
        static string Hash(string path){using(Stream stream=File.OpenRead(path))using(var sha=SHA256.Create())return Hex(sha.ComputeHash(stream));}
        static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        static Vector3[] WorldVertices(SkinnedMeshRenderer skin,Mesh mesh)
        {
            // Verified against normalized world hierarchies: false already includes lossy scale.
            // Applying TransformPoint here would multiply parent scale a second time.
            skin.BakeMesh(mesh,false);Vector3[] vertices=mesh.vertices;Matrix4x4 rigid=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
            for(int i=0;i<vertices.Length;i++)vertices[i]=rigid.MultiplyPoint3x4(vertices[i]);return vertices;
        }
        static Transform FindUnique(Transform root,string name){var found=root.GetComponentsInChildren<Transform>(true).Where(x=>x.name==name).ToArray();Require(found.Length==1,"Native anatomical bone not unique/missing: "+name);return found[0];}
        static Bounds BoundsOf(Vector3[] vertices){Require(vertices.Length>0,"Empty skin.");var bounds=new Bounds(vertices[0],Vector3.zero);foreach(Vector3 v in vertices){Require(Finite(v),"Nonfinite skin vertex.");bounds.Encapsulate(v);}return bounds;}
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        static bool Finite(Quaternion v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z)&&Finite(v.w)&&v.x*v.x+v.y*v.y+v.z*v.z+v.w*v.w>.8f;
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static string Argument(string key,string fallback){string[] args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]==key)return args[i+1];return fallback;}
        static void Exit(int code){if(Application.isBatchMode||Environment.GetEnvironmentVariable("OCEAN_COMPANION_BAKE_EXIT")=="1")EditorApplication.Exit(code);}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[MarineCompanionBake] "+message);}
    }
}
#endif
