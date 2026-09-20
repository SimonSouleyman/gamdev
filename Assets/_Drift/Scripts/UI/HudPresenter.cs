using System.Text;
using TMPro;
using UnityEngine;

namespace Drift.UI
{
    public class HudPresenter : MonoBehaviour
    {
        public HudDataBehaviour dataBehaviour;
        public TMP_Text areaLabel;
        public TMP_Text ecosystemLabel;
        [Min(0.05f)] public float refreshInterval = 0.25f;

        IHudDataSource _source;
        float _next;
        readonly StringBuilder _sb = new StringBuilder(128);

        public void SetSource(IHudDataSource source)
        {
            _source = source;
            _next = 0f;
        }

        void OnEnable()
        {
            if (_source == null && dataBehaviour != null) _source = dataBehaviour;
            _next = 0f;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + refreshInterval;
            if (_source == null || !_source.TryGetSnapshot(out var s)) return;

            if (areaLabel != null)
                areaLabel.text = "Area " + s.landArea.ToString("0");

            if (ecosystemLabel != null)
            {
                int land = s.bareCells + s.plainsCells + s.shrubCells + s.woodsCells + s.oldGrowthCells;
                _sb.Clear();
                if (land > 0)
                {
                    float inv = 100f / land;
                    _sb.Append("Plains ").Append(Mathf.RoundToInt(s.plainsCells * inv)).Append("%  ");
                    _sb.Append("Shrub ").Append(Mathf.RoundToInt(s.shrubCells * inv)).Append("%  ");
                    _sb.Append("Woods ").Append(Mathf.RoundToInt((s.woodsCells + s.oldGrowthCells) * inv)).Append('%');
                    if (s.burningCells > 0) _sb.Append("  Fire ").Append(s.burningCells);
                }
                ecosystemLabel.text = _sb.ToString();
            }
        }
    }
}
