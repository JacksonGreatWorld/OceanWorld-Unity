using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater
{
    /// <summary>Owns only authored label text; dynamic presenters refresh after language changes.</summary>
    [DisallowMultipleComponent]
    public sealed class OceanLocalizedLabel : MonoBehaviour
    {
        Text label;
        string source = "";
        internal void SetSource(Text target,string chinese)
        { label=target;source=chinese;OceanLanguage.Register(this);RefreshLanguage(); }
        internal void RefreshLanguage() { if (label != null) label.text=OceanLanguage.Text(source); }
        void OnDestroy() => OceanLanguage.Unregister(this);
    }
}
