// @tags: coin, data, roster, slot-state, save
using System;

namespace Coin.Data
{
    /// <summary>
    /// 슬롯에 현재 앉아있는 코인 인스턴스 (동적 로스터, 설계 §7.7). 런타임/세이브 공용.
    /// 상장폐지 시 currentName·currentBasePrice가 새 코인으로 갱신된다.
    /// </summary>
    [Serializable]
    public class CoinSlotState
    {
        public int slotId;              // 대응 CoinData.id
        public string currentName;      // 현재 코인 영문명 (초기 = defaultName)
        public float currentBasePrice;  // 현재 표시가 (교체 시 basePrice×Random(0.6,1.8))
        public int delistCount;         // 이 슬롯에서 상장폐지된 횟수
    }
}
