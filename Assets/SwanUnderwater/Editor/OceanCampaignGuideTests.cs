#if UNITY_EDITOR || OCEAN_CAMPAIGN_STANDALONE
using System;
using System.Collections.Generic;

namespace SwanUnderwater.EditorTests
{
    public static class OceanCampaignGuideTests
    {
        [Serializable] public sealed class GuideCase { public string id,action,details; public int level; public bool urgent; }
        public static readonly List<GuideCase> Cases=new List<GuideCase>();
        public static int Assertions { get; private set; }
        static void Check(bool ok,string message){Assertions++;if(!ok)throw new InvalidOperationException("Campaign task strip: "+message);}
        static void Capture(string id,OceanCampaignState state,int hint=0)
        {
            var c=OceanCampaignGuide.Describe(state,hint);
            Cases.Add(new GuideCase{id=id,level=state.Current==null?0:state.Current.level,action=c.Action,details=c.Details,urgent=c.Urgent});
            Check(!string.IsNullOrEmpty(c.Action)&&!string.IsNullOrEmpty(c.Details)&&!c.Action.Contains("\n")&&!c.Details.Contains("\n"),"two explicit single-line strings "+id);
            if(state.Current!=null)Check(c.Action.StartsWith("第"+state.Current.level+"关 · ",StringComparison.Ordinal)&&c.Details.Contains(state.UnlimitedTime?"不限时":"剩余时间 "),"level and time labels "+id);
        }
        public static void RunAll()
        {
            Assertions=0;Cases.Clear();var state=new OceanCampaignState();Capture("no-run",state);
            for(int level=1;level<=OceanLevelCatalog.Count;level++)
            {
                state.StartLevel(level);Capture("start-"+level,state);Capture("alternate-"+level,state,1);
                if(state.RequiresShark)
                {
                    if(state.HasMechanic)for(int node=0;node<state.Definition.MechanicTarget;node++)Check(state.RecordMechanicProgress(100000+node),"complete electric objective before gun guide checks");
                    Check(OceanCampaignGuide.Describe(state).Action.Contains("寻找渔枪"),"unowned tool action "+level);
                    Check(state.AcquireTool(),"acquire tool "+level);
                    for(int shot=0;shot<4;shot++){if(state.Current.pendingSpearRecovery)Check(state.TryRecoverSpear(),"recover actual final-tier spear");Check(state.TryFireSpear(),"real fired round");if(shot<3)Check(state.HitShark(),"real hit");}
                    if(state.Current.pendingSpearRecovery)
                    {
                        Capture("recovery-"+level,state);
                        Check(OceanCampaignGuide.Describe(state).Action.Contains("回收")&&OceanCampaignGuide.Describe(state).Details.Contains("16/20"),"recovery action and exact non-refilled ammunition remain explicit");
                        Check(state.TryRecoverSpear(),"recover before gun progress guide check");
                    }
                    Capture("hits-"+level,state);
                    var c=OceanCampaignGuide.Describe(state);
                    Check(c.Action.Contains("最高命中 3/"+state.SharkHitTarget)&&c.Details.Contains("剩余子弹 16/20"),"hit count and ammunition cannot be confused "+level);
                    while(state.Current.sharkHits<state.SharkHitTarget){if(state.Current.pendingSpearRecovery)Check(state.TryRecoverSpear(),"recover between final-tier shots");Check(state.TryFireSpear()&&state.HitShark(),"finish actual boss hit goal");}
                    Capture("stunned-"+level,state);Check(OceanCampaignGuide.Describe(state).Action.Contains("靠近鲨鱼，握拳抓获"),"stunned action is capture, not another shot");
                    Check(state.CatchShark(),"actual boss catch");Capture("caught-"+level,state);
                }
                int firstShell=First(state.ShellTargetMask),firstFish=First(state.FishTargetMask);
                if(firstShell>=0)Check(state.CollectShell(level*100+firstShell,firstShell),"first shell accepted");
                if(firstFish>=0)Check(state.CollectFish(firstFish),"first fish accepted");
                Capture("partial-"+level,state);
                if(state.Definition.OrderedShell&&state.NextShellSpecies>=0)
                    Check(OceanCampaignGuide.Describe(state).Action.Contains("下一种"),"ordered shell next species explicit");
                if(state.Definition.OrderedFish&&state.NextFishSpecies>=0)
                    Check(OceanCampaignGuide.Describe(state).Action.Contains(OceanLevelCatalog.FishNames[state.NextFishSpecies]),"ordered fish names actual next species");
                for(int s=0;s<10;s++)if((state.ShellTargetMask&(1<<s))!=0&&!state.IsConsumed(level*100+s))Check(state.CollectShell(level*100+s,s),"remaining shell accepted");
                for(int s=0;s<10;s++)if((state.FishTargetMask&(1<<s))!=0&&(state.Current.fishMask&(1<<s))==0)Check(state.CollectFish(s),"remaining fish accepted");
                if(state.HasMechanic&&!state.MechanicComplete)for(int node=0;node<state.Definition.MechanicTarget;node++)Check(state.RecordMechanicProgress(100000+node),"actual new mechanic node "+level);
                Check(state.GoalComplete,"actual main objectives complete "+level);Capture("two-stars-"+level,state);
                Check(OceanCampaignGuide.Describe(state).Action.Contains("寻找剩余金币"),"two-star exploration directed to remaining coin");
                for(int slot=0;slot<state.CoinTargetCount;slot++)Check(state.CollectCoin(OceanLevelCatalog.CoinId(slot)),"every actual coin accepted");
                Capture("three-stars-"+level,state);
                Check(OceanCampaignGuide.Describe(state).Action.Contains(level==OceanLevelCatalog.Count?"百关完成":"进入下一关"),"only final level announces full campaign completion");
                state.StartLevel(level);
                if(!state.UnlimitedTime){state.Tick(state.TimeLimitSeconds-30,true);Capture("last-30-seconds-"+level,state);Check(OceanCampaignGuide.Describe(state).Urgent,"last minute stays highlighted");state.Tick(30,true);Capture("expired-"+level,state);Check(OceanCampaignGuide.Describe(state).Action.Contains("时间已到"),"expired guide cannot direct inaccessible collection");}
            }
            state.StartLevel(5);state.AcquireTool();for(int i=0;i<20;i++){state.TryFireSpear();if(i<9)state.HitShark();}
            Check(OceanCampaignGuide.Describe(state).Action.Contains("等待命中结果"),"last projectile may still resolve at zero ammo");Capture("last-projectile",state);
            Check(state.HitShark()&&state.CatchShark(),"last projectile can unlock capture without ammunition");
            state.StartLevel(5);state.AcquireTool();for(int i=0;i<20;i++)state.TryFireSpear();state.FinalizeSpearAmmo(false);Capture("ammo-failed",state);Check(OceanCampaignGuide.Describe(state).Action.Contains("菜单重试"),"ammo failure action");
            CheckOnboarding();
#if UNITY_EDITOR
            UnityEngine.Debug.Log("[OceanCampaignGuideTests] PASS "+Assertions+" assertions; "+Cases.Count+" state snapshots, no headset claim.");
#else
            Console.WriteLine("[OceanCampaignGuideTests] PASS "+Assertions+" assertions; "+Cases.Count+" state snapshots, no headset claim.");
#endif
        }
        static void CheckOnboarding()
        {
            var state=new OceanCampaignState();
            for(int level=1;level<=5;level++)
            {
                state.StartLevel(level);
                string before=Progress(state);
                for(int hint=0;hint<20;hint++)Capture("onboarding-"+level+"-"+hint,state,hint);
                Check(Progress(state)==before,"rotating guidance cannot change progress or pause gameplay "+level);
                Check(OceanCampaignState.Validate(state.Save,out string error),"guidance retains a valid campaign state: "+error);
                if(level==1||level==5)Check(HasHint(state,"双手向后划水"),"swimming is taught initially and when switching away from the weapon "+level);
                Check(!HasHint(state,"转动头部")&&!HasHint(state,"点头"),"hand instructions do not ask for head movement "+level);
            }
            state.StartLevel(1);
            Check(HasHint(state,"微光贝壳")&&HasHint(state,"张开手再握拳")&&HasHint(state,"还缺5种"),"first shell tutorial describes search, approach and grasp");
            Check(state.CollectShell(510,4),"unordered first-level fifth species is accepted before the first species");
            Check(HasHint(state,"还缺4种")&&!HasHint(state,"锥螺"),"remaining target hints remove the acquired shell species");
            for(int species=0;species<4;species++)Check(state.CollectShell(511+species,species),"other first-level species stay unordered");
            Check(state.Stars==2&&OceanCampaignGuide.Describe(state).Action.Contains("寻找剩余金币"),"tutorial yields to actual completion and optional third star");
            Check(state.CollectCoin(OceanLevelCatalog.CoinId(0))&&state.Stars==3,"coin grants the real third star, not a tutorial reward");

            state.StartLevel(2);
            Check(HasHint(state,"草丛礁缝")&&HasHint(state,"重复不加种类")&&HasHint(state,"图示"),"second level teaches habitat, duplicates and target images");
            foreach(string name in new[]{"扇贝","海螺","宝螺","蛤蜊","锥螺","蚶贝","牡蛎","帽贝","蛏子","骨螺"})
                Check(HasHint(state,name),"the second-level cycle can name every missing shell: "+name);
            Check(state.CollectShell(610,9),"second-level player can start with the last species");
            Check(!HasHint(state,"骨螺"),"second-level hints do not direct the player to an acquired species");

            state.StartLevel(3);
            Check(HasHint(state,"缓慢靠近")&&HasHint(state,"张手再握拳")&&HasHint(state,"任意顺序"),"mixed level teaches gentle fish approach without a forced collection order");
            Check(state.CollectFish(2),"mixed-level player can start with the third fish");
            Check(!HasHint(state,"麒麟鱼"),"fish hints remove an acquired species");
            Check(state.CollectFish(0)&&state.CollectFish(1),"all fish can be caught before either shell");
            Check(!HasHint(state,"靠近鱼")&&HasHint(state,"还缺2种贝壳"),"after fish completion the guide focuses on the remaining shells");
            Check(state.CollectShell(710,0)&&state.CollectShell(711,1)&&state.GoalComplete,"mixed level remains completable in any order");

            state.StartLevel(4);
            foreach(string name in new[]{"小丑鱼","蓝倒吊","麒麟鱼","狮子鱼"})Check(HasHint(state,name),"fourth-level guide names the actual target: "+name);
            Check(HasHint(state,"名称与图示")&&HasHint(state,"还缺4种指定鱼"),"fourth-level guide points to images and labels its remaining targets");
            Check(state.CollectFish(3)&&!HasHint(state,"狮子鱼")&&HasHint(state,"还缺3种指定鱼"),"fourth-level remaining names derive from real catches");

            state.StartLevel(5);
            Check(!HasHint(state,"显枪")&&!HasHint(state,"扣扳机"),"unowned weapon guidance cannot skip pickup");
            Check(state.AcquireTool(),"fifth-level actual weapon pickup");
            Check(HasHint(state,"任一手握拳")&&HasHint(state,"翘拇指伸食指显枪")&&HasHint(state,"弯曲食指扣扳机")&&HasHint(state,"松开手枪姿势"),"owned weapon teaches either hand, trigger and return to swimming");
            Check(HasHint(state,"每条鲨鱼独立计数"),"fifth-level guide explains separate shark progress without replacing trigger instruction");
            Check(HasHint(state,"剩余子弹 20/20")&&HasHint(state,"命中 0/10"),"boss hit target and ammunition remain explicitly labeled in every gesture phase");
            for(int i=0;i<10;i++)Check(state.TryFireSpear()&&state.HitShark(),"actual shots reach the capture threshold");
            for(int hint=0;hint<20;hint++)Check(OceanCampaignGuide.Describe(state,hint).Action.Contains("靠近鲨鱼，握拳抓获"),"capture-ready guide never asks for an unnecessary additional shot");
            Check(state.CatchShark()&&state.GoalComplete,"guidance leaves the capture decision to existing campaign rules");
            state.StartLevel(2);state.Tick(state.TimeLimitSeconds,true);
            for(int hint=0;hint<20;hint++)Check(OceanCampaignGuide.Describe(state,hint).Action.Contains("时间已到"),"failure always replaces beginner prompts");
        }
        static bool HasHint(OceanCampaignState state,string text)
        {
            for(int hint=0;hint<20;hint++)
            {var c=OceanCampaignGuide.Describe(state,hint);if(c.Action.Contains(text)||c.Details.Contains(text))return true;}
            return false;
        }
        static string Progress(OceanCampaignState state)
        {
            var r=state.Current;
            return r.level+"|"+r.remainingSeconds+"|"+r.shellMask+"|"+r.fishMask+"|"+r.coinMask+"|"+r.toolOwned+"|"+r.sharkHits+"|"+r.spearShotsFired+"|"+r.sharkCaught+"|"+r.consumedPickupIds.Count+"|"+r.completed+"|"+r.failed+"|"+state.Save.shellMask+"|"+state.Save.fishMask+"|"+state.Save.shark+"|"+string.Join(",",state.Save.bestStars);
        }
        static int First(int mask){for(int i=0;i<10;i++)if((mask&(1<<i))!=0)return i;return -1;}
    }
}
#endif
