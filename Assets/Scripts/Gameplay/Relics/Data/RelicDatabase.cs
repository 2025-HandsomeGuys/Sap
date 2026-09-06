using System.Collections.Generic;
using UnityEngine;

namespace Relic.Data
{
    [CreateAssetMenu(fileName = "RelicDatabase", menuName = "Database/Relic Database")]
    public class RelicDatabase : ScriptableObject
    {
        public static RelicDatabase Instance { get; private set; }

        public List<RelicSO> allRelics;

        private Dictionary<RelicID, RelicSO> _dict;

        private void OnEnable()
        {
            Instance = this;
            _dict = new Dictionary<RelicID, RelicSO>();
            if (allRelics != null)
            {
                foreach (var r in allRelics)
                {
                    if (r != null && r.id != RelicID.None && !_dict.ContainsKey(r.id))
                        _dict.Add(r.id, r);
                }
            }
        }

        public RelicSO GetRelicByID(RelicID id)
        {
            if (_dict == null) OnEnable();
            _dict.TryGetValue(id, out var so);
            return so;
        }
    }
}
