// @tags: market, sound, sfx, ui, override, marker
using UnityEngine;

namespace Market
{
    /// <summary>
    /// MarketButtonSfx가 자동 부여하는 클릭음을 버튼/토글 단위로 바꾸는 마커.
    /// 소리를 바꾸고 싶은 Button/Toggle 오브젝트에 붙이고 kind를 고른다. None = 음소거.
    /// (안 붙이면 기본 매핑 — 버튼=Click, 토글=Tab, 종목/코인카드=Select)
    /// </summary>
    public class MarketSfxOverride : MonoBehaviour
    {
        public MarketUISfx.Kind kind = MarketUISfx.Kind.Click;
    }
}
