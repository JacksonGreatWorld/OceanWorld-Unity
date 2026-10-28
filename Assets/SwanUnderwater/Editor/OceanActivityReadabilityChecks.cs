#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    public static class OceanActivityReadabilityChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool value,string message)
        { Assertions++;if(!value)throw new InvalidOperationException("Activity readability: "+message); }

        public static void RunAll()
        {
            Assertions=0;
            float volume=AudioListener.volume;
            var root=new GameObject("Isolated activity readability fixture");
            try
            {
                var cameraObject=new GameObject("Activity readability camera");
                cameraObject.transform.SetParent(root.transform,false);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
                camera.aspect=16f/9f;camera.fieldOfView=70f;
                camera.transform.position=new Vector3(0,1.65f,-4);
                var hands=root.AddComponent<HandSwimController>();hands.enabled=false;
                var state=new OceanCampaignState();
                var runtime=root.AddComponent<OceanMechanicRuntime>();
                runtime.Initialize(null,camera,hands,state,null);
                foreach (int level in new[]{6,9,14,16,18})
                {
                    state.StartLevel(level);
                    hands.ApplyComfort(OceanComfortProfile.Default,false);
                    runtime.ConfigureLevel(new Vector3(0,0,-4));
                    Bounds[] hitBounds=new Bounds[runtime.VisibleNodeCount];
                    for(int i=0;i<hitBounds.Length;i++)hitBounds[i]=runtime.NodeBounds(i);
                    foreach(int preset in new[]{0,1,2,0})
                    {
                        hands.ApplyComfort(new OceanComfortProfile(2,4,2,false,preset),false);
                        runtime.RefreshCaptionPoseForValidation();
                        var caption=runtime.CaptionRectForValidation;
                        float multiplier=hands.Comfort.MenuScaleMultiplier;
                        Check(Math.Abs(caption.localScale.x/.00070f-multiplier)<.0001f,
                            "existing text preference scales the caption at level "+level);
                        var corners=new Vector3[4];caption.GetWorldCorners(corners);
                        foreach(Vector3 corner in corners)
                        {
                            Vector3 p=camera.WorldToViewportPoint(corner);
                            Check(p.z>camera.nearClipPlane&&p.x>.04f&&p.x<.96f&&p.y>.02f&&p.y<.4f,
                                "caption remains within the lower view band at preset "+preset);
                        }
                        for(int i=0;i<hitBounds.Length;i++)
                        {
                            Check(runtime.NodeBounds(i)==hitBounds[i],"text scaling cannot move or expand world interaction bounds");
                            RectTransform label=runtime.NodeLabelRectForValidation(i);
                            Check(Math.Abs(label.localScale.x/.0015f-multiplier)<.0001f,
                                "world label follows the existing text preference");
                            Check(label.rect.size==new Vector2(340,70),"logical wrapping area remains fixed");
                        }
                        foreach(Text text in runtime.StationRoot.GetComponentsInChildren<Text>(true))
                        {
                            Check(!text.raycastTarget,"text cannot intercept a hand ray");
                            var shadow=text.GetComponent<UnityEngine.UI.Shadow>();
                            Check(shadow!=null&&shadow.effectColor.a>.9f&&shadow.useGraphicAlpha,
                                "actual glyphs retain a bounded contrast shadow");
                            Check(text.preferredHeight<=text.rectTransform.rect.height+1f,
                                "actual caption and label lines fit their fixed boxes");
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root);AudioListener.volume=volume; }
            Debug.Log("OceanActivityReadabilityChecks PASS "+Assertions+
                " assertions; real UI geometry, preference roundtrip, contrast components and unchanged hit bounds. Not headset legibility acceptance.");
        }
    }
}
#endif
