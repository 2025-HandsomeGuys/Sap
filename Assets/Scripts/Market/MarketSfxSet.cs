// @tags: market, sound, sfx, assets, scriptableobject, override, swap
using UnityEngine;

namespace Market
{
    /// <summary>
    /// 마켓 UI 효과음을 코드 합성 대신 **직접 가져온 오디오 에셋**으로 교체하는 세트.
    ///
    /// 사용법:
    ///  1. Project 창에서 [Create > Market > SFX Set] 으로 이 에셋을 만든다.
    ///  2. 각 슬롯에 AudioClip을 드래그한다. (비워두면 그 소리는 기존 코드 합성음으로 폴백)
    ///  3. MarketSceneController의 "효과음 에셋(sfxSet)" 슬롯에 이 에셋을 연결한다.
    ///
    /// 슬롯에 든 클립을 바꾸거나, 세트 에셋을 통째로 갈아끼우면 즉시 다른 소리로 교체된다.
    /// 각 슬롯은 MarketUISfx.Kind 한 종류에 대응한다.
    /// </summary>
    [CreateAssetMenu(fileName = "MarketSfxSet", menuName = "Market/SFX Set", order = 0)]
    public class MarketSfxSet : ScriptableObject
    {
        [Header("UI 상호작용 (비우면 합성 클릭음)")]
        [Tooltip("타이핑 / 코인 드럼롤 틱")] public AudioClip type;
        [Tooltip("기본 버튼 클릭 (수량 조절 등)")] public AudioClip click;
        [Tooltip("카드/항목 선택 (클릭)")] public AudioClip select;
        [Tooltip("목록 휠 스크롤 리플 (비우면 부드러운 합성 리플)")] public AudioClip scroll;
        [Tooltip("주식/코인 탭 전환")] public AudioClip tab;

        [Header("거래 결과 (비우면 합성 차임)")]
        [Tooltip("매수/매도·베팅 성공")] public AudioClip confirm;
        [Tooltip("거래 실패/거부")] public AudioClip deny;

        [Header("코인 결과 (비우면 합성 레트로음)")]
        [Tooltip("코인 적중/떡상")] public AudioClip win;
        [Tooltip("폭락/상장폐지")] public AudioClip crash;
        [Tooltip("코인 드럼롤 대기 '띠' 비프 (비우면 합성 비프)")] public AudioClip blip;

        [Header("물리 마우스 클릭 — 씬 전역 (비우면 합성 클릭음)")]
        [Tooltip("마우스 클릭음 (버튼을 뗄 때 1회)")] public AudioClip mouseUp;

        [Header("공통")]
        [Range(0f, 4f)]
        [Tooltip("이 세트의 지정 클립에 곱해지는 볼륨. 1=원음, >1=키움(가져온 에셋이 작을 때), <1=줄임. 너무 키우면 클리핑 주의")]
        public float volume = 1f;

        /// <summary>해당 Kind에 지정된 클립 (없으면 null → 코드 합성음 폴백).</summary>
        public AudioClip Clip(MarketUISfx.Kind kind)
        {
            switch (kind)
            {
                case MarketUISfx.Kind.Type:    return type;
                case MarketUISfx.Kind.Click:   return click;
                case MarketUISfx.Kind.Select:  return select;
                case MarketUISfx.Kind.Scroll:  return scroll;
                case MarketUISfx.Kind.Tab:     return tab;
                case MarketUISfx.Kind.Confirm: return confirm;
                case MarketUISfx.Kind.Deny:    return deny;
                case MarketUISfx.Kind.Win:     return win;
                case MarketUISfx.Kind.Crash:   return crash;
                case MarketUISfx.Kind.Blip:    return blip;
                default: return null;
            }
        }
    }
}
