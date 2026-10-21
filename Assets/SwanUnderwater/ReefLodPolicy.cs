namespace SwanUnderwater
{
    /// <summary>Pure distance rule; Unity keeps selecting economical screen-space LODs outside the near zone.</summary>
    public static class ReefLodPolicy
    {
        public const float EnterMetres=8f, ExitMetres=9f;
        public static int SelectForcedLod(float distanceSquared,bool wasNear,bool remoteRidge)
        {
            int fallback=remoteRidge?1:-1;
            if(float.IsNaN(distanceSquared)||float.IsInfinity(distanceSquared)||distanceSquared<0f)return fallback;
            bool near=wasNear?distanceSquared<ExitMetres*ExitMetres:distanceSquared<=EnterMetres*EnterMetres;
            return near?0:fallback;
        }
    }
}
