using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Authored model identity, not image classification or a gameplay collision target.</summary>
    public sealed class OceanNaturalistTag : MonoBehaviour
    {
        public int Species { get; private set; } = -1;
        public void Initialize(int species) { Species = species; }
    }

}
