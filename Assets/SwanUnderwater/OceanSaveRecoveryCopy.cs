namespace SwanUnderwater
{
    /// <summary>Recovery describes verified reads, never a promise to repair or overwrite a file.</summary>
    public static class OceanSaveRecoveryCopy
    {
        public const string ReplacesUnsaved = "重新读取会替换本次未保存进度；原文件保留。";
        public const string SceneFailed = "场景暂未准备好。\n原存档未变，可重新读取。";
        public const string SpearInFlight = "鱼叉正在飞行。\n请返回本关，等待鱼叉结束后再重新读取。\n本次进度与原文件保留。";
        public static string Describe(OceanSaveLoadStatus status, bool readSucceeded)
        {
            if (readSucceeded)
                return (status == OceanSaveLoadStatus.RecoveredBackup ? "已从通过检查的备用存档重新读取。" : "已重新读取通过检查的存档。") +
                    "\n本次保持只读，不修复或覆盖原文件。\n可返回探索；正常重启后再确认保存。";
            switch (status)
            {
                case OceanSaveLoadStatus.UnsupportedVersion:
                    return "存档版本暂不支持。\n原文件已保留，本次只读。\n请使用支持该存档的版本再试。";
                case OceanSaveLoadStatus.NewSave:
                    return "未找到原存档。\n本次保留当前进度，不生成新存档。\n请先保留外部备用存档，再重新读取。";
                case OceanSaveLoadStatus.IoError:
                    return "存档暂时无法读取。\n请检查存档位置与空间后再试。\n原文件已保留，本次只读。";
                default:
                    return "存档无法通过检查。\n原文件已保留，本次只读。\n可重新读取；再次失败请保留文件并联系支持。";
            }
        }
    }
}
