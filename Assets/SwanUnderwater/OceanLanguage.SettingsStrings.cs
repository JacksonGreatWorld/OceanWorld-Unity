using System.Collections.Generic;
namespace SwanUnderwater
{
    public static partial class OceanLanguage
    {
        static void AddSettingsStrings(Dictionary<string,string> entries)
        {
            entries["绘制"]="Draw";
            entries["三角"]="Tri";
            entries["切换"]="Pass";
            entries["语言 / Language"]="Language";
            entries["{0} {1}"]="{0} {1}";
            entries["{0}  {1}/{2}{3}"]="{0}  {1}/{2}{3}";
            entries[" · 握拳抓取"]="\nClose hand to catch";
            entries["贝壳{0}种"]="Shells {0}";
            entries["鱼{0}种"]="Fish {0}";
            entries["单鲨{0}次命中后抓取"]="One shark: {0} hits, catch";
            entries["依图鉴顺序"]="In guide order";
            entries["字级 / 声音 / 性能"]="Text / Audio / Stats";
            entries["开始 / 重看四步教学"]="Replay 4-step lesson";
            entries["关闭教学，继续探索"]="Close lesson & explore";
            entries["移动速度：{0}（点击切换）"]="Swim speed: {0} (tap to change)";
            entries["全部声音：{0}（点击切换）"]="Master volume: {0} (tap to change)";
            entries["人工转向：{0}（点击切换）"]="Snap turn: {0} (tap to change)";
            entries["划水姿势：{0}（点击切换）"]="Swim posture: {0} (tap to change)";
            entries["菜单字级：{0}（点击切换）"]="Text size: {0} (tap to change)";
            entries["{0}：{1}（点击切换）"]="{0}: {1} (tap to change)";
            entries["静音"]="Muted";entries["开启"]="On";
            entries["坐姿 · 小幅动作"]="Seated / small strokes";entries["站姿 / 常规"]="Standing / regular";
            entries["教学暂停本关时钟，不改变星级、收藏或弹药。\n"]="Lessons pause the clock. Progress and ammo stay unchanged.\n";
            entries["坐姿无需弯腰拾取练习物；保持周围有安全活动空间。\n手追不可用时，可用控制器射线打开菜单和退出；任务抓取需要手追。"]="Seated practice needs no bending. Keep your play area clear.\nController rays can open menus and exit. Collecting needs hand tracking.";
            entries["环境：海底背景 · 音乐：结局配乐 · 反馈：收集与渔枪\n字级同步放大菜单、文字和点击区域，保留原排版"]="Ambience: ocean · Music: finale · Feedback: pickups and speargun\nText size scales the menu, labels and hit areas together.";
            entries["跟随系统（{0}）"]="Follow system ({0})";
            entries["英文"]="English";entries["中文"]="Chinese";
            entries[" · 已选"]=" · Selected";
            entries["默认跟随系统语言；无法识别时，中国地区用中文，其他地区用英文。\n手动选择立即生效并保存。可随时恢复跟随系统，关卡与收藏不会改变。"]="Follows your system language. If unsupported, China uses Chinese; other regions use English.\nYour choice applies now and is saved. Progress and collections stay unchanged.";
        }
    }
}
