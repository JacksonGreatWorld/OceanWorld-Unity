using System;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>Original noninteractive hand-status and slow-motion teaching graphic. Never writes real tracking or lesson progress.</summary>
    public sealed class OceanTutorialVisual : MonoBehaviour
    {
        readonly OceanTutorialStrokeDemo demo = new OceanTutorialStrokeDemo();
        readonly RectTransform[] palms = new RectTransform[2];
        readonly Text[] statuses = new Text[2];
        readonly Image[][] fingers = new Image[2][];
        GameObject root;
        Text instruction, demonstration;
        Material material;
        bool visible;
        static readonly Color Tracked = new Color(.30f,.93f,.77f), Missing = new Color(.95f,.61f,.28f), Ghost = new Color(.37f,.77f,.94f);

        public void Begin(Camera view, Font font)
        {
            Close(); demo.Reset();
            if (view == null || font == null) throw new ArgumentNullException("Tutorial graphic camera/font");
            Shader shader = Resources.Load<Shader>("PerformanceHudUI");
            if (shader == null) throw new InvalidOperationException("Existing tutorial UI shader is missing.");
            material = new Material(shader) { name = "Original tutorial diagram", renderQueue = 4120 };
            root = new GameObject("Tutorial hand status and slow demonstration",typeof(RectTransform),typeof(Canvas));
            root.transform.SetParent(view.transform,false); root.transform.localPosition = new Vector3(0,-.065f,.80f);
            root.transform.localScale = Vector3.one * .00070f;
            ((RectTransform)root.transform).sizeDelta = new Vector2(600,245);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = view; canvas.overrideSorting = true; canvas.sortingOrder = 32755;
            Image panel = Image(root.transform,"Diagram contrast",Vector2.zero,new Vector2(600,245),new Color(.008f,.035f,.055f,.92f));
            panel.raycastTarget = false;
            for (int i=0;i<2;i++)
            {
                float x = i == 0 ? -145 : 145;
                statuses[i] = Label(root.transform,font,"Fresh hand status " + i,"",new Vector2(x,89),new Vector2(275,42),22);
                var palm = new GameObject("Illustrated " + (i==0?"left":"right") + " hand",typeof(RectTransform));
                palm.transform.SetParent(root.transform,false); palms[i]=(RectTransform)palm.transform;
                palms[i].anchoredPosition = new Vector2(x,0); palms[i].sizeDelta = new Vector2(80,92);
                fingers[i]=new Image[6];
                fingers[i][0]=Image(palm.transform,"Palm",new Vector2(0,-8),new Vector2(48,46),Tracked);
                for(int finger=0;finger<4;finger++)
                    fingers[i][finger+1]=Image(palm.transform,"Finger " + finger,new Vector2(-18+finger*12,29-(finger==0||finger==3?4:0)),new Vector2(8,32-(finger==0||finger==3?8:0)),Tracked);
                fingers[i][5]=Image(palm.transform,"Thumb",new Vector2(i==0?31:-31,-2),new Vector2(24,10),Tracked);
                fingers[i][5].rectTransform.localRotation=Quaternion.Euler(0,0,i==0?30:-30);
            }
            demonstration=Label(root.transform,font,"Graphic identity","",new Vector2(0,-68),new Vector2(570,36),18);
            instruction=Label(root.transform,font,"Single teaching instruction","",new Vector2(0,-100),new Vector2(580,36),18);
            root.SetActive(false);
        }

        public void Render(OceanInteractiveTutorialRules.Step current, bool leftTracked, bool rightTracked, bool leftOpen, bool rightOpen, float dt, bool show)
        {
            if(root==null)return;
            bool swim=current==OceanInteractiveTutorialRules.Step.SwimAndStop;
            visible=show && current>=OceanInteractiveTutorialRules.Step.Tracking && current<=OceanInteractiveTutorialRules.Step.Menu;
            demo.Advance(dt,visible&&swim);
            root.SetActive(visible);
            for(int i=0;i<2;i++)
            {
                bool tracked=i==0?leftTracked:rightTracked,open=i==0?leftOpen:rightOpen;
                statuses[i].text=OceanLanguage.Format("{0}：{1}",OceanLanguage.Text(i==0?"左手":"右手"),
                    OceanLanguage.Text(tracked?(open?"已追踪 · 张开":"已追踪 · 请张手"):"未追踪 · 请伸手"));
                statuses[i].color=tracked?Tracked:Missing;
                float x=i==0?-145:145;
                // This moves only an explicitly labelled icon, never a sampled palm or the XR Origin.
                palms[i].anchoredPosition=new Vector2(x,swim?25f-demo.PullFraction*52f:0f);
                Color tint=swim?Color.Lerp(Ghost,new Color(.34f,.54f,.63f),demo.Recovering?.45f:0f):tracked?Tracked:Missing;
                foreach(var image in fingers[i])image.color=tint;
            }
            demonstration.text=OceanLanguage.Text(swim?"慢动作示意 · 图中的手不是实时追踪":"左右手追踪状态 · 至少一只手张开");
            instruction.text=OceanLanguage.Text(swim?(demo.Holding?"停手，等待减速；示意动画不会完成教学":demo.Recovering?"轻松把手送回前方，再向身后划水":"双手缓慢向身后划水，不必快速挥动"):
                current==OceanInteractiveTutorialRules.Step.PracticeGrab?"靠近练习扇贝，先张开手，再握拳":current==OceanInteractiveTutorialRules.Step.Menu?"打开金色菜单，查看目标，再收起":"请把手放到头显能看见的范围内");
        }
        public void SetVisible(bool value)
        { visible=value; if(root!=null)root.SetActive(value); }
        public void Close()
        {
            visible=false;
            if(root!=null){root.SetActive(false);DestroyOwned(root);}root=null;
            if(material!=null)DestroyOwned(material);material=null;
        }
        Image Image(Transform parent,string name,Vector2 position,Vector2 size,Color colour)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            var image=go.GetComponent<Image>();image.material=material;image.color=colour;image.raycastTarget=false;
            image.rectTransform.anchoredPosition=position;image.rectTransform.sizeDelta=size;return image;
        }
        Text Label(Transform parent,Font font,string name,string value,Vector2 position,Vector2 size,int pixels)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            var text=go.GetComponent<Text>();text.font=font;text.fontSize=pixels;text.material=material;OceanLanguage.Bind(text,value);text.color=Color.white;
            text.raycastTarget=false;text.alignment=TextAnchor.MiddleCenter;text.supportRichText=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.rectTransform.anchoredPosition=position;text.rectTransform.sizeDelta=size;return text;
        }
        static void DestroyOwned(UnityEngine.Object value)
        {if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        void OnDisable()=>SetVisible(false);
        void OnDestroy()=>Close();
#if UNITY_EDITOR
        public bool VisibleForValidation=>root!=null&&root.activeSelf;
        public bool HasGraphicForValidation=>root!=null;
        public string LeftStatusForValidation=>statuses[0]!=null?statuses[0].text:"";
        public string RightStatusForValidation=>statuses[1]!=null?statuses[1].text:"";
        public string DemonstrationForValidation=>demonstration!=null?demonstration.text:"";
        public float DemoSecondsForValidation=>demo.Seconds;
        public Text[] TextForValidation=>root!=null?root.GetComponentsInChildren<Text>(true):Array.Empty<Text>();
        public RectTransform GraphicRectForValidation=>root!=null?(RectTransform)root.transform:null;
#endif
    }
}
