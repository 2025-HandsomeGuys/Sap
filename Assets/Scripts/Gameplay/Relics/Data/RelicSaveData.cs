using System;
using System.Collections.Generic;

namespace Relic.Data
{
    [Serializable]
    public struct OwnedRelic
    {
        public RelicID id;
        public int level;   // 1..maxLevel

        public OwnedRelic(RelicID id, int level)
        {
            this.id = id;
            this.level = level;
        }
    }

    [Serializable]
    public class RelicSaveData
    {
        public bool hasData = false;                        // 세이브 존재 플래그 (Coin/Stock 컨벤션)
        public int slotCount = 0;                           // 업그레이드(RelicSlotUp)로만 열린다
        public List<OwnedRelic> owned = new List<OwnedRelic>();
        public List<RelicID> loadout = new List<RelicID>(); // 슬롯 순서. 빈 슬롯 = RelicID.None

        // ── 탐험 드롭 피티(pity) 카운터 ──
        // 유물은 확률 드롭이라 운이 나쁘면 한 회차 내내 0개일 수 있다. 미획득 상태가 이어진
        // 횟수를 세어 두고 임계치를 넘기면 다음 번을 확정으로 만든다.
        // 구버전 세이브엔 이 필드가 없다 → JsonUtility가 0으로 채우므로 그대로 호환된다.
        public int rockDropPity  = 0;   // 유물 없이 완파한 돌 수
        public int chestDropPity = 0;   // 유물 없이 연 던전 상자 수
    }
}
