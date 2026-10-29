#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    // Explicit derivative of the admitted source. Never runs from import/domain hooks.
    // Bind-space widening precedes skinning, so it does not amplify the authored tail arc.
    public static class OceanMackerelVolumeBake
    {
        public const string Resource="MarineExpansion/mackerel-volume";
        const string Original="MarineExpansion/mackerel";
        const string MeshPath="Assets/ArtBaked/MarineExpansion/mackerel-volume/mackerel-volume.asset";
        const string PrefabPath="Assets/Resources/MarineExpansion/mackerel-volume.prefab";
        const string GeometryPath="Assets/Resources/MarineExpansion/mackerel-volume-geometry.json";
        const string ReceiptPath="Sources/MarineExpansion/mackerel-volume-admission.json";
        const string PublishedReceiptHash="a8725632c9d42da957ead4fee4bcbd6c6b7d183bd792ddad6d49e3614876ccab";
        const string NativeReceiptPath="Sources/MarineExpansion/mackerel-native-admission.json";
        const string NativeProjectionHash="d7fa3e40d04a99bd2d50f84a76038ba01d5a6487b52c65e4a60e8d089a3c239d";
        const string OriginalNativeReceiptHash="04354d82616c5200a25b3f02a91f7130547804dec778a760b9c1fdf8c5b04172";
        const string Policy="bind-y-volume-v1: 1+1.4*smooth(.12,.35,u)*(1-.65*smooth(.87,1,u))*smooth(.003,.013,abs(y)); head=+x; preserve x/z, near-plane fins and tail; inverse-transpose normals; original rig/maps/UV";
        [Serializable] sealed class FileHash {public string path,sha256;public FileHash(string p){path=p;sha256=Hash(p);}}
        [Serializable] sealed class PublishedNativeIdentity
        { public bool publicationProjection=false; public string sourceReportSha256=""; }
        [Serializable] sealed class Receipt
        {
            public int schemaVersion=1,vertices,triangles,poseSamples,unchangedFinOrTailVertices,assertions;
            public bool success;
            public string policy=Policy,unity,utc,sourceUrl="https://sketchfab.com/3d-models/atlantic-mackerel-animated-and-game-ready-eca6071c26fc4013b8c94a3f37a434c7",
                author="Ishxxn (@1shxxn)",license="CC-BY-4.0",limitations="Art-directed volume correction, not measured biological anatomy. Same topology/rig and maps; Editor evidence does not establish headset acceptance.";
            public List<FileHash> inputs=new List<FileHash>(),outputs=new List<FileHash>();
        }
        static int assertions;
        static void Check(bool value,string message){assertions++;if(!value)throw new InvalidOperationException("Mackerel volume: "+message);}
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        static IEnumerable<string> SourceFiles()
        {
            foreach(string folder in new[]{"Assets/ArtSources/MarineExpansion/mackerel","Assets/ArtBaked/MarineExpansion/mackerel"})
                foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal))yield return file.Replace('\\','/');
            foreach(string file in new[]{"Assets/Resources/MarineExpansion/mackerel.prefab","Assets/Resources/MarineExpansion/mackerel.prefab.meta",
                "Assets/Resources/MarineExpansion/mackerel-geometry.json","Assets/Resources/MarineExpansion/mackerel-geometry.json.meta","Sources/MarineExpansion/mackerel-native-admission.json"})yield return file;
        }
        static float Smooth(float a,float b,float value){float t=Mathf.Clamp01((value-a)/(b-a));return t*t*(3f-2f*t);}
        static Vector3 Map(Vector3 p,float min,float length)
        {
            float u=(p.x-min)/length;
            if(u<=.12f||Mathf.Abs(p.y)<=.003f)return p;
            float weight=Smooth(.12f,.35f,u)*(1f-.65f*Smooth(.87f,1f,u))*Smooth(.003f,.013f,Mathf.Abs(p.y));
            return new Vector3(p.x,p.y*(1f+1.4f*weight),p.z);
        }
        public static Mesh CreateVolumeMesh(Mesh source)
        {
            if(source==null||!source.isReadable)throw new InvalidOperationException("Readable admitted source mesh required");
            Mesh result=Object.Instantiate(source);result.name="Atlantic mackerel body-volume derivative";
            Vector3[] original=source.vertices,vertices=(Vector3[])original.Clone(),normals=source.normals;Vector4[] tangents=source.tangents;
            float min=original.Min(p=>p.x),length=original.Max(p=>p.x)-min;const float epsilon=.00001f;
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 p=original[i];vertices[i]=Map(p,min,length);
                if(vertices[i]==p)continue;
                float dx=(Map(p+Vector3.right*epsilon,min,length).y-Map(p-Vector3.right*epsilon,min,length).y)/(2f*epsilon);
                float dy=(Map(p+Vector3.up*epsilon,min,length).y-Map(p-Vector3.up*epsilon,min,length).y)/(2f*epsilon);
                if(!(dy>.1f))throw new InvalidOperationException("Invalid deformation Jacobian");
                Vector3 n=normals[i];n=new Vector3(n.x-dx*n.y/dy,n.y/dy,n.z).normalized;normals[i]=n;
                Vector4 old=tangents[i];Vector3 t=new Vector3(old.x,dx*old.x+dy*old.y,old.z);t=(t-n*Vector3.Dot(n,t)).normalized;
                tangents[i]=new Vector4(t.x,t.y,t.z,old.w);
            }
            result.vertices=vertices;result.normals=normals;result.tangents=tangents;result.RecalculateBounds();return result;
        }
        [MenuItem("Swan/Codex/Marine Expansion/Bake mackerel body volume derivative")]
        public static void Bake()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode,"EditMode only");
            SwanUnderwater.Editor.OceanMarineExpansionBake.ValidateForBuild();
            if(File.Exists(ReceiptPath)){ValidateForBuild();return;}
            Check(!File.Exists(MeshPath)&&!File.Exists(PrefabPath)&&!File.Exists(GeometryPath),"never replace partial or existing derivative assets");
            var receipt=new Receipt{unity=Application.unityVersion,utc=DateTime.UtcNow.ToString("O")};foreach(string path in SourceFiles())receipt.inputs.Add(new FileHash(path));
            GameObject original=Resources.Load<GameObject>(Original);Check(original!=null,"source prefab");
            Mesh source=original.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));AssetDatabase.Refresh();
            Mesh derivative=CreateVolumeMesh(source);AssetDatabase.CreateAsset(derivative,MeshPath);
            GameObject model=Object.Instantiate(original);
            try
            {
                model.name="Atlantic Mackerel body volume";var skin=model.GetComponentInChildren<SkinnedMeshRenderer>(true);skin.sharedMesh=derivative;
                var geometry=Measure(model,out Bounds localEnvelope);skin.localBounds=localEnvelope;
                geometry.sourceMeshSha256=Hash(MeshPath);File.WriteAllText(GeometryPath,JsonUtility.ToJson(geometry,true));AssetDatabase.ImportAsset(GeometryPath,ImportAssetOptions.ForceSynchronousImport);
                // Store the authored rest hierarchy, not the last sampled animation pose.
                Object.DestroyImmediate(model);model=Object.Instantiate(original);model.name="Atlantic Mackerel body volume";
                skin=model.GetComponentInChildren<SkinnedMeshRenderer>(true);skin.sharedMesh=derivative;skin.localBounds=localEnvelope;
                Check(PrefabUtility.SaveAsPrefabAsset(model,PrefabPath)!=null,"derivative prefab saves");AssetDatabase.SaveAssets();
                receipt.vertices=derivative.vertexCount;receipt.triangles=derivative.triangles.Length/3;receipt.poseSamples=61;
                Vector3[] a=source.vertices,b=derivative.vertices;float min=a.Min(p=>p.x),length=a.Max(p=>p.x)-min;
                for(int i=0;i<a.Length;i++)if((a[i].x-min)/length<=.12f||Mathf.Abs(a[i].y)<=.003f){Check(a[i]==b[i],"thin-fin/tail negative control");receipt.unchangedFinOrTailVertices++;}
                foreach(string path in new[]{MeshPath,MeshPath+".meta",PrefabPath,PrefabPath+".meta",GeometryPath,GeometryPath+".meta"})receipt.outputs.Add(new FileHash(path));
                receipt.success=true;receipt.assertions=assertions;File.WriteAllText(ReceiptPath,JsonUtility.ToJson(receipt,true));
            }
            finally{if(model!=null)Object.DestroyImmediate(model);}
            ValidateForBuild();
        }
        static OceanAuthoredFishGeometry Measure(GameObject model,out Bounds localEnvelope)
        {
            var skin=model.GetComponentInChildren<SkinnedMeshRenderer>(true);var animator=model.GetComponentInChildren<Animator>(true);var mesh=new Mesh();
            var geometry=new OceanAuthoredFishGeometry();Bounds envelope=default;localEnvelope=default;
            try
            {
                animator.Rebind();animator.Update(0);int state=animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                for(int pose=0;pose<=60;pose++)
                {
                    animator.Play(state,0,pose==60?.9999f:pose/60f);animator.Update(0);skin.BakeMesh(mesh,false);
                    Matrix4x4 rigid=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);var first=true;Bounds body=default;
                    foreach(Vector3 vertex in mesh.vertices)
                    {
                        Vector3 world=rigid.MultiplyPoint3x4(vertex),point=model.transform.InverseTransformPoint(world);
                        if(first){body=new Bounds(point,Vector3.zero);first=false;}else body.Encapsulate(point);
                    }
                    Check(body.size.magnitude>.01f&&body.size.magnitude<100f,"finite posed body");
                    if(pose==0){geometry.firstPoseDiagonal=body.size.magnitude;envelope=body;}else envelope.Encapsulate(body);
                    Vector3[] vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++){Vector3 point=skin.transform.InverseTransformPoint(rigid.MultiplyPoint3x4(vertices[i]));if(pose==0&&i==0)localEnvelope=new Bounds(point,Vector3.zero);else localEnvelope.Encapsulate(point);}
                }
                envelope.Expand(geometry.firstPoseDiagonal*.02f);localEnvelope.Expand(localEnvelope.size.magnitude*.02f);
                geometry.navigationCenter=envelope.center;geometry.navigationSize=envelope.size;geometry.sourceMeshSha256=new string('0',64);Check(geometry.IsValid,"sampled derivative navigation envelope");return geometry;
            }
            finally{Object.DestroyImmediate(mesh);}
        }
        public static void ValidateForBuild()
        {
            SwanUnderwater.Editor.OceanMarineExpansionBake.ValidateForBuild();
            ValidateAssets(false);
        }
        /// <summary>Validate the unchanged derivative admission and geometry against the sealed public source projection.
        /// Only the original native receipt is represented by its exact projection; all other input/output bytes remain required.</summary>
        public static void ValidatePublishedAssets()
        {
            SwanUnderwater.Editor.OceanMarineExpansionBake.ValidatePublishedAssets();
            ValidateAssets(true);
        }
        static void ValidateAssets(bool published)
        {
            assertions=0;Check(File.Exists(ReceiptPath),"explicit derivative admission required");var receipt=JsonUtility.FromJson<Receipt>(File.ReadAllText(ReceiptPath));
            if(published)Check(Hash(ReceiptPath)==PublishedReceiptHash,"exact original derivative admission, not a new or rewritten receipt");
            Check(receipt!=null&&receipt.success&&receipt.schemaVersion==1&&receipt.policy==Policy&&receipt.poseSamples==61,"sealed derivative policy");
            Check(receipt.inputs.Select(row=>row.path).SequenceEqual(SourceFiles()),"complete ordered original input roster, no omissions or duplicates");
            Check(receipt.outputs.Select(row=>row.path).SequenceEqual(new[]{MeshPath,MeshPath+".meta",PrefabPath,PrefabPath+".meta",GeometryPath,GeometryPath+".meta"}),"complete fixed derivative output roster");
            foreach(var row in receipt.inputs.Concat(receipt.outputs))
            {
                if(published&&row.path==NativeReceiptPath)
                {
                    Check(row.sha256==OriginalNativeReceiptHash&&File.Exists(row.path)&&Hash(row.path)==NativeProjectionHash,
                        "only the exact original native admission may use its sealed public projection");
                    var identity=JsonUtility.FromJson<PublishedNativeIdentity>(File.ReadAllText(row.path));
                    Check(identity!=null&&identity.publicationProjection&&identity.sourceReportSha256==row.sha256,
                        "native projection identifies the exact original derivative input receipt");
                }
                else Check(File.Exists(row.path)&&Hash(row.path)==row.sha256,"exact source/output bytes: "+row.path);
            }
            var original=Resources.Load<GameObject>(Original);var prefab=Resources.Load<GameObject>(Resource);Check(original!=null&&prefab!=null,"both original and derivative retained");
            var a=original.GetComponentInChildren<SkinnedMeshRenderer>(true);var b=prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);Mesh expected=CreateVolumeMesh(a.sharedMesh);
            try
            {
                Check(a.sharedMesh!=b.sharedMesh&&b.sharedMesh==AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath),"explicit derived mesh only");
                Check(a.sharedMaterials.SequenceEqual(b.sharedMaterials),"original PBR maps/material slots");Check(a.sharedMesh.triangles.SequenceEqual(b.sharedMesh.triangles)&&a.sharedMesh.uv.SequenceEqual(b.sharedMesh.uv),"topology/UV unchanged");
                Check(a.sharedMesh.bindposes.SequenceEqual(b.sharedMesh.bindposes),"original bind poses unchanged");
                var countsA=a.sharedMesh.GetBonesPerVertex();var countsB=b.sharedMesh.GetBonesPerVertex();
                Check(countsA.Length==countsB.Length,"full skin influence-count roster");
                for(int i=0;i<countsA.Length;i++)Check(countsA[i]==countsB[i],"all influences per vertex unchanged");
                var weightsA=a.sharedMesh.GetAllBoneWeights();var weightsB=b.sharedMesh.GetAllBoneWeights();
                Check(weightsA.Length==weightsB.Length,"full skin weight roster");
                for(int i=0;i<weightsA.Length;i++)Check(weightsA[i].boneIndex==weightsB[i].boneIndex&&weightsA[i].weight==weightsB[i].weight,"all bone indices and weights unchanged");
                Check(expected.vertices.SequenceEqual(b.sharedMesh.vertices)&&expected.normals.SequenceEqual(b.sharedMesh.normals)&&expected.tangents.SequenceEqual(b.sharedMesh.tangents),"deterministic volume/normals/tangents");
                Check(original.GetComponentInChildren<Animator>(true).runtimeAnimatorController==prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController,"same authored swim controller");
                Vector3[] source=a.sharedMesh.vertices,changed=b.sharedMesh.vertices;int changedCount=0,retained=0;float min=source.Min(p=>p.x),length=source.Max(p=>p.x)-min;
                for(int i=0;i<source.Length;i++)
                {
                    Check(source[i].x==changed[i].x&&source[i].z==changed[i].z,"side silhouette axes exact");if(!source[i].Equals(changed[i]))changedCount++;
                    if((source[i].x-min)/length<=.12f||Mathf.Abs(source[i].y)<=.003f){Check(source[i].Equals(changed[i]),"actual thin-fin/tail vertices remain exact");retained++;}
                }
                Check(retained==receipt.unchangedFinOrTailVertices&&retained>source.Length/3,"thin-fin/tail negative control is populated");
                Check(changedCount>source.Length/4,"body actually has added volume");
                GameObject instance=Object.Instantiate(prefab);try{var measured=Measure(instance,out Bounds unused);var stored=JsonUtility.FromJson<OceanAuthoredFishGeometry>(Resources.Load<TextAsset>(Resource+"-geometry").text);Check(stored.IsValid&&stored.sourceMeshSha256==Hash(MeshPath),"geometry identifies derived mesh");Check(Mathf.Abs(measured.firstPoseDiagonal-stored.firstPoseDiagonal)<.00001f&&(measured.navigationCenter-stored.navigationCenter).sqrMagnitude<1e-10f&&(measured.navigationSize-stored.navigationSize).sqrMagnitude<1e-10f,"61-pose metadata remeasured");}finally{Object.DestroyImmediate(instance);}
            }
            finally{Object.DestroyImmediate(expected);}
            Debug.Log((published?"MACKEREL_VOLUME_PUBLISHED_ASSETS_PASS":"MACKEREL_VOLUME_ADMISSION_PASS")+" assertions="+assertions+"; same topology/rig/maps, exact fin/tail negative controls, 61 real poses; no wearer claim");
        }
    }
}
#endif
