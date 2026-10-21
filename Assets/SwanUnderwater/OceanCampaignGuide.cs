using System;

namespace SwanUnderwater
{
    /// <summary>Pure, state-derived task strip copy. Hints never mutate goals or impose order on unordered levels.</summary>
    public static class OceanCampaignGuide
    {
        public readonly struct Copy
        {
            public readonly string Action, Details;
            public readonly bool Urgent;
            public Copy(string action,string details,bool urgent=false){Action=action;Details=details;Urgent=urgent;}
        }
        static readonly string[] ShellNames={"扇贝","海螺","宝螺","蛤蜊","锥螺","蚶贝","牡蛎","帽贝","蛏子","骨螺"};
        public static Copy DescribeTutorial(OceanInteractiveTutorialRules tutorial, string practiceHint = "")
        {
            const string paused = "关卡计时暂停 · 菜单→舒适设置可跳过";
            switch (tutorial.Current)
            {
                case OceanInteractiveTutorialRules.Step.Tracking:
                    return new Copy("教学 1/4 · 伸出一只手并张开，保持片刻", "手要在头显视野内 · " + paused);
                case OceanInteractiveTutorialRules.Step.SwimAndStop:
                    return new Copy("教学 2/4 · 向后划水两次，再停手等待减速", "有效划水 " + Math.Min(2,tutorial.PracticeStrokes) + "/2 · " + paused);
                case OceanInteractiveTutorialRules.Step.PracticeGrab:
                    return new Copy("教学 3/4 · 靠近面前练习扇贝，张手再握拳", string.IsNullOrEmpty(practiceHint) ? paused : practiceHint);
                case OceanInteractiveTutorialRules.Step.Menu:
                    return new Copy("教学 4/4 · 打开菜单→查看本关目标→收起", tutorial.TargetsViewed ? "目标已查看 · 收起菜单完成教学" : tutorial.MenuOpened ? "菜单已打开 · 请查看本关目标" : "练习扇贝已拾取 · 指向金色菜单条，松开后捏合");
                default: return new Copy("教学已结束 · 开始海底探索", "舒适设置可随时重看教学");
            }
        }
        public static Copy Describe(OceanCampaignState state,int hintIndex=0)
        {
            if(state==null||state.Current==null)return new Copy("海底世界 · 选择关卡开始探险","100关探索 · 捏合右侧菜单");
            var run=state.Current;string prefix="第"+run.level+"关 · ";
            string time=state.UnlimitedTime?"不限时":"剩余时间 "+Clock(run.remainingSeconds);
            string coin="金币 "+state.CoinCount+"/"+state.CoinTargetCount;
            int shells=Count(run.shellMask&state.ShellTargetMask),shellTotal=Count(state.ShellTargetMask);
            int fish=Count(run.fishMask&state.FishTargetMask),fishTotal=Count(state.FishTargetMask);
            hintIndex=Math.Max(0,hintIndex);
            string progress=(shellTotal>0?"贝壳 "+shells+"/"+shellTotal+" · ":"")+(fishTotal>0?"鱼 "+fish+"/"+fishTotal+" · ":"")+coin+" · "+time;
            bool urgent=!state.UnlimitedTime&&run.remainingSeconds<60&&!run.timedOut;
            if(state.Failed)return new Copy(prefix+(state.AmmoExhausted?"子弹用尽 · 菜单重试本关":"时间已到 · 菜单重试本关"),progress,true);
            if(state.RequiresSpearRecovery&&run.pendingSpearRecovery&&!run.sharkCaught&&run.sharkHits<state.SharkHitTarget)
                return new Copy(prefix+"等待鱼叉系回身前 · 握抓或松手后捏合回收",
                    "最高命中 "+run.sharkHits+"/"+state.SharkHitTarget+" · 回收不补弹 · 剩余子弹 "+state.SpearAmmoRemaining+"/"+state.ShotLimit+" · "+progress,urgent);
            if(state.GoalComplete)
            {
                string action=!state.CanCollect?"本关结束 · 菜单选择关卡":state.CoinCount<state.CoinTargetCount?"目标完成 · 寻找剩余金币 "+(state.CoinTargetCount-state.CoinCount)+"枚":run.level==OceanLevelCatalog.Count?"百关完成 · 继续探索海底":"本关完成 · 菜单进入下一关";
                return new Copy(prefix+action,coin+" · "+time,urgent);
            }
            if(state.HasMechanic&&!state.MechanicComplete)
                return new Copy(prefix+state.Definition.MechanicHint,
                    "探险 "+run.mechanicProgress+"/"+state.Definition.MechanicTarget+" · "+progress,urgent);
            if(state.RequiresShark&&!run.sharkCaught)
            {
                string action=!run.toolOwned?"先寻找渔枪，再挑战鲨鱼":run.sharkHits>=state.SharkHitTarget?"靠近鲨鱼，握拳抓获":state.SpearAmmoRemaining==0?"子弹用尽，等待命中结果":"最高命中 "+run.sharkHits+"/"+state.SharkHitTarget;
                string ammunition=run.toolOwned?"剩余子弹 "+state.SpearAmmoRemaining+"/"+state.ShotLimit+" · ":"";
                // These hints describe existing gestures; they never hold gameplay or consume ammunition.
                if(run.level==5)
                {
                    if(!run.toolOwned)
                    {
                        switch(hintIndex%4)
                        {
                            case 1:action="在沙地寻找渔枪 · 留意工具微光";break;
                            case 2:action="靠近渔枪 · 张开手再握拳拾取";break;
                            case 3:action="双手向后划水 · 探索地面工具";break;
                        }
                    }
                    else if(run.sharkHits<state.SharkHitTarget&&state.SpearAmmoRemaining>0)
                    {
                        switch(hintIndex%5)
                        {
                            case 1:action="任一手握拳 · 翘拇指伸食指显枪";break;
                            case 2:action="瞄准鲨鱼 · 弯曲食指扣扳机";break;
                            case 3:action="每条鲨鱼独立计数 · 瞄准再扣扳机";break;
                            case 4:action="松开手枪姿势 · 双手向后划水";break;
                        }
                        // Keep both durable counts visible while the action line teaches the gesture.
                        ammunition="最高命中 "+run.sharkHits+"/"+state.SharkHitTarget+" · "+ammunition;
                    }
                }
                return new Copy(prefix+action,ammunition+time+" · "+coin,urgent);
            }
            if(state.Definition.OrderedShell&&shells<shellTotal)
                return new Copy(prefix+"依次找贝壳 · 下一种"+ShellNames[state.NextShellSpecies],progress,urgent);
            if(state.Definition.OrderedFish&&fish<fishTotal)
                return new Copy(prefix+"依次抓鱼 · 下一种"+OceanLevelCatalog.FishNames[state.NextFishSpecies],progress,urgent);
            int shellMissing=state.ShellTargetMask&~run.shellMask,fishMissing=state.FishTargetMask&~run.fishMask;
            if(run.level<=4)
                return new Copy(prefix+FirstLevelsHint(run.level,shellMissing,fishMissing,hintIndex),progress,urgent);
            bool showShell=shellMissing!=0&&(fishMissing==0||hintIndex%2==0);
            int slot=shellMissing!=0&&fishMissing!=0?hintIndex/2:hintIndex;
            string hint=showShell?"寻找贝壳，如"+ShellNames[SpeciesAt(shellMissing,slot)]:fishMissing!=0?"握拳抓鱼，如"+OceanLevelCatalog.FishNames[SpeciesAt(fishMissing,slot)]:"查看菜单中的本关目标";
            return new Copy(prefix+hint,progress,urgent);
        }
        static string FirstLevelsHint(int level,int shellMissing,int fishMissing,int hintIndex)
        {
            int step=hintIndex%4,slot=hintIndex/4;
            string shell=shellMissing==0?"":ShellNames[SpeciesAt(shellMissing,slot)];
            string fish=fishMissing==0?"":OceanLevelCatalog.FishNames[SpeciesAt(fishMissing,slot)];
            if(level==1)
            {
                switch(step)
                {
                    case 0:return "双手向后划水前进 · 寻找微光贝壳";
                    case 1:return "靠近"+shell+" · 张开手再握拳";
                    case 2:return "还缺"+Count(shellMissing)+"种贝壳 · 如"+shell;
                    default:return "沙地与珊瑚边找贝壳 · 任意顺序";
                }
            }
            if(level==2)
            {
                switch(step)
                {
                    case 0:return "草丛礁缝找"+shell+" · 还缺"+Count(shellMissing)+"种";
                    case 1:return "靠近微光 · 张开手再握拳拾取";
                    case 2:return "换区域找"+ShellNames[SpeciesAt(shellMissing,slot+5)]+" · 重复不加种类";
                    default:return "缺少的贝壳图示 · 展开菜单查看";
                }
            }
            if(level==3)
            {
                switch(step)
                {
                    case 0:return fishMissing!=0?"缓慢靠近"+fish+" · 张手再握拳":"还缺"+Count(shellMissing)+"种贝壳 · 如"+shell;
                    case 1:return shellMissing!=0?"靠近"+shell+" · 张开手再握拳":"还缺"+Count(fishMissing)+"种鱼 · 如"+fish;
                    case 2:return fishMissing!=0?"靠近鱼后再握拳 · 不必追着挥手":"留意沙地微光 · 继续寻找贝壳";
                    default:return "贝壳与鱼可任意顺序 · 图示见菜单";
                }
            }
            switch(step)
            {
                case 0:return "还缺"+Count(fishMissing)+"种指定鱼 · 如"+fish;
                case 1:return "缓慢靠近"+fish+" · 张手再握拳";
                case 2:return "指定鱼名称与图示 · 展开菜单查看";
                default:return "任意顺序收集 · 已有鱼不重复计种";
            }
        }
        static int SpeciesAt(int mask,int index)
        {
            index%=Math.Max(1,Count(mask));
            for(int i=0;i<10;i++)if((mask&(1<<i))!=0&&index--==0)return i;
            return 0;
        }
        static int Count(int mask){int n=0;while(mask!=0){mask&=mask-1;n++;}return n;}
        static string Clock(float seconds)
        {int value=(int)Math.Ceiling(Math.Max(0,seconds));return(value/60).ToString("00")+":"+(value%60).ToString("00");}
    }
}
