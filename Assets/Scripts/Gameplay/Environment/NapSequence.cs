// @tags: environment, sleep, nap, fade, save, stock

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Sap.UI.Notification;
using Stock.Core;

/// <summary>
/// 낮잠 시퀀스 한 벌. <b>수면(<see cref="SleepSequence"/>)과 다르다</b> —
/// 낮잠은 "다음 주식 틱만 지나가되 하루는 끝나지 않는" 짧은 휴식이다.
///
/// 페이드아웃 → 코고는 소리 → <b>주식 1틱</b>(<see cref="StockGameManager.ProcessTick"/>) → 저장 → 페이드인.
///
/// <b>수면과의 차이(의도적):</b>
///  · 날짜·시간대(오전/오후)를 바꾸지 않는다 (<see cref="DayCycleManager.AdvanceToNextDay"/> 미호출).
///  · 하루 정산 연출(DaySummaryUI)·장부 리셋(<see cref="DayEarningsLedger"/>)이 없다 — 하루가 안 끝났으니까.
///  · 틱으로 시세·강제매각 정산이 바뀌므로 저장은 한다(수면과 동일).
///
/// <b>페이드는 자체 오버레이로 처리한다</b> — 수면은 페이드 뒤 DaySummaryUI(검은 정산 화면)가
/// 화면을 덮어 <see cref="ScreenFader"/>가 없어도 티가 안 나지만, 낮잠엔 그게 없어서
/// ScreenFader에 의존하면 씬에 그게 없을 때 "화면이 안 어두워지는" 문제가 생긴다. 그래서
/// 코드로 만든 검은 오버레이를 직접 페이드한다(씬 세팅 불필요).
/// </summary>
public static class NapSequence
{
    // 수면(SleepSequence)과 같은 페이드 길이 — 낮잠도 화면이 자연스럽게 어두워졌다 밝아진다.
    private const float FadeDuration = 0.6f;

    /// <summary>코고는 소리 음량(0~1). <see cref="SleepSequence"/>와 같은 값.</summary>
    private const float SnoreVolume = 0.5f;

    /// <summary>낮잠 암전 유지 시간(초). 페이드 사이의 "자는" 텀.</summary>
    private const float RestHold = 0.6f;

    /// <summary>진행 중인지. 두 침대(또는 낮잠·수면)가 겹쳐 재생되는 것을 막는다.</summary>
    public static bool IsPlaying { get; private set; }

    // 자체 페이드 오버레이(한 번 만들어 재사용, 평소 alpha 0).
    private static CanvasGroup _fadeGroup;

    /// <summary>
    /// 낮잠을 재생한다. 호출부에서 <c>yield return NapSequence.Run()</c> 로 기다린다.
    /// 낮잠 횟수 소비는 이 코루틴이 <see cref="NapManager.ConsumeNap"/>로 직접 처리한다.
    /// </summary>
    public static IEnumerator Run()
    {
        if (IsPlaying || SleepSequence.IsPlaying) yield break;

        // 횟수 소비 — 실패하면(횟수 없음) 아무 것도 하지 않는다.
        if (NapManager.Instance == null || !NapManager.Instance.ConsumeNap())
            yield break;

        IsPlaying = true;

        // 자는 동안 플레이어가 움직이지 못하게 UIState를 잠근다(플레이어 코드는 IsInputBlocked을 이미 본다).
        // 지금 None일 때만 잡고, 끝나면 되돌린다 — 다른 상태를 덮어쓰지 않게.
        var ui = UIStateManager.Instance;
        bool lockedInput = ui != null && ui.CurrentState == UIState.None;
        if (lockedInput) ui.SetState(UIState.BedRest);

        // 1) 페이드아웃 — 화면을 검게 덮고 시작
        yield return FadeTo(1f);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.SleepSnore, 1f, SnoreVolume);

        // 2) 주식 1틱만 진행 (날짜·시간대는 그대로) — 시세·뉴스 갱신 + 인내심 강제매각 정산
        if (StockGameManager.Instance != null && StockGameManager.Instance.IsInitialized)
            StockGameManager.Instance.ProcessTick();

        // 암전 유지 (짧게 눈 붙이는 느낌)
        yield return new WaitForSecondsRealtime(RestHold);

        // 3) 자동 저장 — 변동된 시세·낮잠 사용량이 세이브에 반영되도록
        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
            GameManager.Instance.saveManager.Save();

        // 4) 페이드인
        yield return FadeTo(0f);

        // 입력 잠금 해제 (내가 잡았고 아직 BedRest면 되돌린다)
        if (lockedInput && ui != null && ui.CurrentState == UIState.BedRest)
            ui.SetState(UIState.None);

        if (NotificationUI.Instance != null)
        {
            int left = NapManager.Instance != null ? NapManager.Instance.RemainingNaps : 0;
            NotificationUI.Instance.ShowNotification($"잠깐 눈을 붙였다. (남은 낮잠 {left}회)");
        }

        IsPlaying = false;
    }

    // ===================================================
    // 자체 페이드 오버레이
    // ===================================================

    private static IEnumerator FadeTo(float target, float duration = FadeDuration)
    {
        EnsureFadeOverlay();

        float start = _fadeGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // timeScale과 무관하게 항상 재생
            _fadeGroup.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        _fadeGroup.alpha = target;
    }

    private static void EnsureFadeOverlay()
    {
        if (_fadeGroup != null) return;

        var go = new GameObject("NapFadeOverlay");
        Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000; // 게임 화면 위, 일시정지(30800)보다는 아래

        _fadeGroup = go.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;
        _fadeGroup.blocksRaycasts = false;
        _fadeGroup.interactable = false;

        var imgGo = new GameObject("Black", typeof(RectTransform));
        imgGo.transform.SetParent(go.transform, false);
        var img = imgGo.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
