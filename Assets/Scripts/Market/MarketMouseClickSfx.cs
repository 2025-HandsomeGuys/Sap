// @tags: market, sound, sfx, mouse, click, crt, monitor
using UnityEngine;

namespace Market
{
    /// <summary>
    /// MarketScene 전용 — 마우스 버튼을 뗄 때(클릭 완료) 물리 클릭음을 한 번 재생해
    /// "모니터(CRT 터미널)에서 딸깍" 하는 촉감을 준다.
    /// 위젯 UI 사운드(MarketUISfx.Play)와는 독립된 오디오 소스로 겹쳐 재생된다.
    ///
    /// MarketSceneController가 Awake에서 자동 부착하므로 에디터 셋업이 필요 없고,
    /// 이 컴포넌트는 MarketScene이 로드된 동안에만 존재한다 → 다른 씬엔 클릭음이 새지 않는다.
    /// </summary>
    public class MarketMouseClickSfx : MonoBehaviour
    {
        [Tooltip("오른쪽 버튼 클릭에도 소리를 낼지")]
        [SerializeField] private bool includeRightButton = false;

        private void Update()
        {
            if (Input.GetMouseButtonUp(0) || (includeRightButton && Input.GetMouseButtonUp(1)))
                MarketUISfx.PlayMouseClick();
        }
    }
}
