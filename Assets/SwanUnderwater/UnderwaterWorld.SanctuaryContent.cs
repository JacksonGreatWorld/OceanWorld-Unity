namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld
    {
        void CreateSanctuaryContent()
        {
            if (!BuildingSanctuary || ConstructingBiome != OceanCampaignBiome.Reef) return;
            OceanSanctuaryMicroLife.Build(EnvironmentParent, mainCamera.transform,
                SampleRenderedSeabedHeight, ActiveSpawn, ReefHabitat.BubbleSources, bubbleMaterial);
            OceanSanctuaryDiscovery.Build(EnvironmentParent, this, mainCamera);
        }
    }
}
