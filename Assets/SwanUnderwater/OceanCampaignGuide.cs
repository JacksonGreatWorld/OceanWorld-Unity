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
        static string L(string source)=>OceanLanguage.Text(source);
        static string F(string source,params object[] values)=>OceanLanguage.Format(source,values);
        public static Copy DescribeTutorial(OceanInteractiveTutorialRules tutorial, string practiceHint = "")
        {
            string paused = L("关卡计时暂停 · 随时点击关闭教学");
            switch (tutorial.Current)
            {
                case OceanInteractiveTutorialRules.Step.Tracking:
                    return new Copy(L("教学 1/4 · 伸出一只手并张开，保持片刻"), F("手要在头显视野内 · {0}",paused));
                case OceanInteractiveTutorialRules.Step.SwimAndStop:
                    return new Copy(L("教学 2/4 · 向后划水两次，再停手等待减速"), F("有效划水 {0}/2 · {1}",Math.Min(2,tutorial.PracticeStrokes),paused));
                case OceanInteractiveTutorialRules.Step.PracticeGrab:
                    return new Copy(L("教学 3/4 · 靠近面前练习扇贝，张手再握拳"), string.IsNullOrEmpty(practiceHint) ? paused : L(practiceHint));
                case OceanInteractiveTutorialRules.Step.Menu:
                    return new Copy(L("教学 4/4 · 打开菜单→查看本关目标"), L(tutorial.TargetsViewed ? "目标已查看 · 点完成教学返回游戏" : tutorial.MenuOpened ? "菜单已打开 · 请查看本关目标" : "练习扇贝已拾取 · 指向金色菜单条，松开后捏合"));
                default: return new Copy(L("教学已结束 · 开始海底探索"), L("舒适设置可随时重看教学"));
            }
        }
        public static Copy Describe(OceanCampaignState state,int hintIndex=0)
        {
            if(state==null||state.Current==null)return new Copy(L("海底世界 · 选择关卡开始探险"),L("前20关探索 · 捏合右侧菜单"));
            var run=state.Current;string prefix=F("第{0}关 · ",run.level);
            string time=state.UnlimitedTime?L("不限时"):F("剩余时间 {0}",Clock(run.remainingSeconds));
            string coin=F("金币 {0}/{1}",state.CoinCount,state.CoinTargetCount);
            int shells=Count(run.shellMask&state.ShellTargetMask),shellTotal=Count(state.ShellTargetMask);
            int fish=Count(run.fishMask&state.FishTargetMask),fishTotal=Count(state.FishTargetMask);
            hintIndex=Math.Max(0,hintIndex);
            string progress=Progress(shells,shellTotal,fish,fishTotal,coin,time);
            bool urgent=!state.UnlimitedTime&&run.remainingSeconds<60&&!run.timedOut;
            if(state.Failed)return new Copy(prefix+L(state.AmmoExhausted?"子弹用尽 · 菜单重试本关":"时间已到 · 菜单重试本关"),progress,true);
            if(state.RequiresSpearRecovery&&run.pendingSpearRecovery&&!run.sharkCaught&&run.sharkHits<state.SharkHitTarget)
                return new Copy(prefix+L("等待鱼叉系回身前 · 握抓或松手后捏合回收"),
                    F("最高命中 {0}/{1} · 回收不补弹 · 剩余子弹 {2}/{3} · {4}",run.sharkHits,state.SharkHitTarget,state.SpearAmmoRemaining,state.ShotLimit,progress),urgent);
            if(state.GoalComplete)
            {
                if(state.Definition.MechanicOptional&&!state.MechanicComplete&&state.CanCollect&&hintIndex%3==2)
                    return new Copy(prefix+OptionalAction(state.Definition.Mechanic),
                        F("主目标已完成 · 支线 {0}/{1} · {2} · {3}",run.mechanicProgress,state.Definition.MechanicTarget,coin,CompactTime(state)),urgent);
                string action=!state.CanCollect?L("本关结束 · 菜单选择关卡"):state.CoinCount<state.CoinTargetCount?F("目标完成 · 寻找剩余金币 {0}枚",state.CoinTargetCount-state.CoinCount):L(OceanCampaignAvailability.IsFinalLevel(run.level)?"本章完成 · 后续敬请期待":run.level==OceanLevelCatalog.Count?"百关完成 · 继续探索海底":"本关完成 · 菜单进入下一关");
                return new Copy(prefix+action,coin+" · "+time,urgent);
            }
            if(state.HasMechanic&&!state.MechanicComplete&&(!state.Definition.MechanicOptional||hintIndex%3==2))
                return new Copy(prefix+(state.Definition.MechanicOptional?OptionalAction(state.Definition.Mechanic):L(state.Definition.MechanicHint)),
                    F(state.Definition.MechanicOptional?"支线 {0}/{1} · {2}":"探险 {0}/{1} · {2}",run.mechanicProgress,state.Definition.MechanicTarget,
                    state.Definition.MechanicOptional?Progress(shells,shellTotal,fish,fishTotal,coin,CompactTime(state)):progress),urgent);
            if(state.RequiresShark&&!run.sharkCaught)
            {
                string action=!run.toolOwned?L("先寻找渔枪，再挑战鲨鱼"):run.sharkHits>=state.SharkHitTarget?L("靠近鲨鱼，握拳抓获"):state.SpearAmmoRemaining==0?L("子弹用尽，等待命中结果"):F("最高命中 {0}/{1}",run.sharkHits,state.SharkHitTarget);
                string ammunition=run.toolOwned?F("剩余子弹 {0}/{1} · ",state.SpearAmmoRemaining,state.ShotLimit):"";
                // These hints describe existing gestures; they never hold gameplay or consume ammunition.
                if(run.level==5)
                {
                    if(!run.toolOwned)
                    {
                        switch(hintIndex%4)
                        {
                            case 1:action=L("在沙地寻找渔枪 · 留意工具微光");break;
                            case 2:action=L("靠近渔枪 · 张开手再握拳拾取");break;
                            case 3:action=L("双手向后划水 · 探索地面工具");break;
                        }
                    }
                    else if(run.sharkHits<state.SharkHitTarget&&state.SpearAmmoRemaining>0)
                    {
                        switch(hintIndex%5)
                        {
                            case 1:action=L("任一手握拳 · 翘拇指伸食指显枪");break;
                            case 2:action=L("瞄准鲨鱼 · 弯曲食指扣扳机");break;
                            case 3:action=L("每条鲨鱼独立计数 · 瞄准再扣扳机");break;
                            case 4:action=L("松开手枪姿势 · 双手向后划水");break;
                        }
                        // Keep both durable counts visible while the action line teaches the gesture.
                        ammunition=F("最高命中 {0}/{1} · {2}",run.sharkHits,state.SharkHitTarget,ammunition);
                    }
                }
                return new Copy(prefix+action,ammunition+time+" · "+coin,urgent);
            }
            if(state.Definition.OrderedShell&&shells<shellTotal)
                return new Copy(prefix+F("依次找贝壳 · 下一种{0}",L(ShellNames[state.NextShellSpecies])),progress,urgent);
            if(state.Definition.OrderedFish&&fish<fishTotal)
                return new Copy(prefix+F("依次抓鱼 · 下一种{0}",L(OceanLevelCatalog.FishNames[state.NextFishSpecies])),progress,urgent);
            int shellMissing=state.ShellTargetMask&~run.shellMask,fishMissing=state.FishTargetMask&~run.fishMask;
            if(run.level<=4)
                return new Copy(prefix+FirstLevelsHint(run.level,shellMissing,fishMissing,hintIndex),progress,urgent);
            bool showShell=shellMissing!=0&&(fishMissing==0||hintIndex%2==0);
            int slot=shellMissing!=0&&fishMissing!=0?hintIndex/2:hintIndex;
            string hint=showShell?F("寻找贝壳，如{0}",L(ShellNames[SpeciesAt(shellMissing,slot)])):fishMissing!=0?F("握拳抓鱼，如{0}",L(OceanLevelCatalog.FishNames[SpeciesAt(fishMissing,slot)])):L("查看菜单中的本关目标");
            return new Copy(prefix+hint,progress,urgent);
        }
        // The compact task strip is one readable line. The full authored hint is
        // retained on the objective card and the world station instead of truncating it.
        static string OptionalAction(OceanMechanicKind kind)
        {
            switch(kind)
            {
                case OceanMechanicKind.RingRace:return L("可选支线 · 沿沙谷依次穿环");
                case OceanMechanicKind.Observation:return L("可选支线 · 稳定观察后捏合记录");
                case OceanMechanicKind.RunePuzzle:return L("可选支线 · 记住亮起顺序再捏合");
                case OceanMechanicKind.CurrentRiding:return L("可选支线 · 导流叶旁张手划动");
                case OceanMechanicKind.Escort:return L("可选支线 · 张手引海龟到圈内");
                default:return L("可选支线 · 展开菜单查看目标");
            }
        }
        static string FirstLevelsHint(int level,int shellMissing,int fishMissing,int hintIndex)
        {
            int step=hintIndex%4,slot=hintIndex/4;
            string shell=shellMissing==0?"":L(ShellNames[SpeciesAt(shellMissing,slot)]);
            string fish=fishMissing==0?"":L(OceanLevelCatalog.FishNames[SpeciesAt(fishMissing,slot)]);
            if(level==1)
            {
                switch(step)
                {
                    case 0:return L("双手向后划水前进 · 寻找微光贝壳");
                    case 1:return F("靠近{0} · 张开手再握拳",shell);
                    case 2:return F("还缺{0}种贝壳 · 如{1}",Count(shellMissing),shell);
                    default:return L("沙地与珊瑚边找贝壳 · 任意顺序");
                }
            }
            if(level==2)
            {
                switch(step)
                {
                    case 0:return F("草丛礁缝找{0} · 还缺{1}种",shell,Count(shellMissing));
                    case 1:return L("靠近微光 · 张开手再握拳拾取");
                    case 2:return F("换区域找{0} · 重复不加种类",L(ShellNames[SpeciesAt(shellMissing,slot+5)]));
                    default:return L("缺少的贝壳图示 · 展开菜单查看");
                }
            }
            if(level==3)
            {
                switch(step)
                {
                    case 0:return fishMissing!=0?F("缓慢靠近{0} · 张手再握拳",fish):F("还缺{0}种贝壳 · 如{1}",Count(shellMissing),shell);
                    case 1:return shellMissing!=0?F("靠近{0} · 张开手再握拳",shell):F("还缺{0}种鱼 · 如{1}",Count(fishMissing),fish);
                    case 2:return L(fishMissing!=0?"靠近鱼后再握拳 · 不必追着挥手":"留意沙地微光 · 继续寻找贝壳");
                    default:return L("贝壳与鱼可任意顺序 · 图示见菜单");
                }
            }
            switch(step)
            {
                case 0:return F("还缺{0}种指定鱼 · 如{1}",Count(fishMissing),fish);
                case 1:return F("缓慢靠近{0} · 张手再握拳",fish);
                case 2:return L("指定鱼名称与图示 · 展开菜单查看");
                default:return L("任意顺序收集 · 已有鱼不重复计种");
            }
        }
        static string CompactTime(OceanCampaignState state)=>state.UnlimitedTime?L("不限时"):F("剩余 {0}",Clock(state.Current.remainingSeconds));
        static string Progress(int shells,int shellTotal,int fish,int fishTotal,string coin,string time)=>
            (shellTotal>0?F("贝壳 {0}/{1} · ",shells,shellTotal):"")+(fishTotal>0?F("鱼 {0}/{1} · ",fish,fishTotal):"")+coin+" · "+time;
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
