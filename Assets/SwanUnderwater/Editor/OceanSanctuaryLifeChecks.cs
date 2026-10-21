#if UNITY_EDITOR
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Invoke after a prepared sanctuary is ready. Not executed during the shared-device hold;
    /// source compilation alone does not validate corridors, visible fish motion or frame time.
    /// </summary>
    public static class OceanSanctuaryLifeChecks
    {
        public static int RunAll(UnderwaterWorld world)
        {
            if (world == null) throw new System.ArgumentNullException(nameof(world));
            int assertions = world.CheckSanctuaryLifeForEditor();
            Debug.Log("OceanSanctuaryLifeChecks PASS " + assertions +
                " assertions; expected-formation corridors, detached flee/recovery, member convergence and pause behavior. " +
                "Individual fish mesh collisions, rendered animation and PICO performance remain separate checks.");
            return assertions;
        }
    }
}
#endif
