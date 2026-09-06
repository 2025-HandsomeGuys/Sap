// @tags: coin, sound, sfx, helper
using Market;

namespace Coin.UI
{
    /// <summary>
    /// 코인 미니게임 연출용 SFX 안전 재생 헬퍼.
    /// SoundDataSO에 등록된 클립이 있으면 그것을 재생하고,
    /// 없으면 합성 UI 효과음(MarketUISfx)으로 폴백한다 — 에셋 없이도 소리가 난다.
    /// 사운드 디자이너가 아래 이름의 클립을 추가하면 코드 수정 없이 실제 음원으로 교체된다.
    /// </summary>
    public static class CoinSfx
    {
        public const string Bet    = "coin_bet";    // 베팅 확정 클릭
        // 드럼롤은 SfxKeys에도 등록돼 있다 — 리터럴을 두 곳에 두면 갈라져서 무음이 된다.
        public const string Tick   = SfxKeys.CoinTick; // 가속 '띠' 비프 (간격·피치 상승)
        public const string Win    = "coin_win";    // 일반 적중
        public const string Lose   = "coin_lose";   // 일반 빗나감
        public const string Moon   = "coin_moon";   // 떡상(+100%)
        public const string Crash  = "coin_crash";  // 폭락(−100%, 베팅 적중)
        public const string Delist = "coin_delist"; // 상장폐지

        /// <summary>pitch는 합성 폴백(MarketUISfx)에만 적용된다 — 대기 연출 피치 상승용.
        /// SoundDataSO 등록 클립은 SoundManager 경로라 원음으로 재생된다.</summary>
        public static void Play(string soundName, float pitch = 1f)
        {
            // Tick(대기 연출)만 예외: SoundDataSO에 coin_tick(드럼롤 mp3)이 등록돼 있어도 쓰지 않는다.
            // SoundManager 경로는 pitch를 못 실어서 드럼롤이 같은 음으로 반복되며 가속감이 죽는다.
            // 원래 소리인 합성 '띠' 비프(MarketUISfx.Kind.Blip)로 고정한다.
            if (soundName != Tick)
            {
                var sm = SoundManager.Instance;
                if (sm != null && sm.HasSFX(soundName)) { sm.PlaySFX(soundName); return; }
            }
            MarketUISfx.Play(Fallback(soundName), pitch);
        }

        /// <summary>클립 미등록 시 사용할 합성음 매핑.</summary>
        private static MarketUISfx.Kind Fallback(string soundName)
        {
            switch (soundName)
            {
                case Bet:    return MarketUISfx.Kind.Confirm;
                case Tick:   return MarketUISfx.Kind.Blip;
                case Win:    return MarketUISfx.Kind.Win;
                case Moon:   return MarketUISfx.Kind.Win;
                case Lose:   return MarketUISfx.Kind.Deny;
                case Crash:  return MarketUISfx.Kind.Crash;
                case Delist: return MarketUISfx.Kind.Crash;
                default:     return MarketUISfx.Kind.None;
            }
        }
    }
}
