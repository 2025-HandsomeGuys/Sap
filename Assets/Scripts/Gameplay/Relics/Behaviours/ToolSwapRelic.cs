using System;

namespace Relic
{
    // 도구 역할 스왑(패시브·강화 없음): 장착 중 삽=돌, 곡괭이=땅으로 채굴 대상이 뒤바뀐다.
    // 능력 판정의 단일 소스(ToolCapabilities)만 토글하므로 전 채굴 경로(Pickaxe/Sap/Digger)와
    // 도구 능력을 참조하는 다른 유물이 자동으로 뒤바뀐 능력을 따른다.
    [Serializable]
    public class ToolSwapRelic : RelicBehaviour
    {
        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            ToolCapabilities.SwapTerrainRock = true;
        }

        public override void OnUnequip()
        {
            ToolCapabilities.SwapTerrainRock = false;
        }
    }
}
