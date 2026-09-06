// @tags: interaction, prompt, localization, color, resolver, text
using UnityEngine;

/// <summary>
/// 근접 안내 문구의 공용 팔레트·해석 규칙.
/// 오브젝트 위 라벨(WorldInteractable 피드백)과 화면 하단 HUD 문구
/// (<see cref="PlayerInteractor"/>)가 같은 색·같은 문자열을 쓰도록 한 곳에 모아둔다.
/// 색을 바꾸려면 이 파일만 고치면 된다(CodeUIKit 팔레트와 같은 취급).
/// </summary>
public static class InteractionPrompts
{
    // ── 팔레트 ──
    /// <summary>지금 상호작용 가능 — 따뜻한 미색.</summary>
    public static readonly Color AvailableColor = new Color(1f, 0.95f, 0.78f);

    /// <summary>조건 불충족 — 바랜 붉은 회색(읽히되 "안 된다"가 보이게).</summary>
    public static readonly Color BlockedColor = new Color(0.86f, 0.55f, 0.55f);

    /// <summary>문구 뒤 어두운 판. 밝은 지형 위에서도 읽히게 한다.
    /// 코드 생성 오버레이(창고·설정·도감)의 카드 배경 <c>CodeUI.CardBg</c>(#18223C)와 같은 톤을 쓴다 —
    /// 월드 위에 뜨는 판만 혼자 다른 색이면 UI가 두 벌처럼 보인다. 지형 위라 알파만 살짝 낮춘다.</summary>
    public static readonly Color BackdropColor = new Color(CodeUI.CardBg.r, CodeUI.CardBg.g, CodeUI.CardBg.b, 0.92f);

    public static Color ColorOf(bool available) => available ? AvailableColor : BlockedColor;

    /// <summary>
    /// 문구를 현재 언어로 해석한다. 키가 없거나 못 찾으면 폴백 원문을 쓴다.
    /// </summary>
    public static string Resolve(in InteractionPromptInfo info)
    {
        if (!info.HasText) return string.Empty;

        var lm = LanguageManager.Instance;
        string text = info.Fallback;

        if (lm != null && !string.IsNullOrEmpty(info.Key))
        {
            string localized = lm.L(info.Key);
            // LocalizationProvider는 키를 못 찾으면 키 자체를 돌려준다 → 폴백으로 되돌린다.
            if (!string.IsNullOrEmpty(localized) && localized != info.Key)
                text = localized;
        }

        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (info.Args == null || info.Args.Length == 0) return text;

        try
        {
            return string.Format(text, info.Args);
        }
        catch (System.FormatException)
        {
            return text; // 포맷 인자 불일치는 원문 그대로 (게임을 멈출 이유는 없음)
        }
    }
}

/// <summary>
/// 한 GameObject의 프롬프트 제공자들을 한 번만 찾아 캐시해두고 매 프레임 싸게 조회한다.
/// 매 프레임 GetComponents를 부르면 배열이 계속 할당되므로 반드시 이걸 통해 조회할 것.
///
/// 우선순위: <see cref="IInteractionPrompt"/> → <see cref="IInteractable.InteractionPrompt"/>(+
/// <see cref="IInteractionAvailability"/>). 덕분에 기존 오브젝트는 코드 수정 없이도 라벨이 뜬다.
/// </summary>
public sealed class InteractionPromptSource
{
    private readonly IInteractionPrompt[] _providers;
    private readonly IInteractable[] _interactables;
    private readonly IInteractionAvailability[] _availability;

    public InteractionPromptSource(GameObject go)
    {
        if (go == null)
        {
            _providers = System.Array.Empty<IInteractionPrompt>();
            _interactables = System.Array.Empty<IInteractable>();
            _availability = System.Array.Empty<IInteractionAvailability>();
            return;
        }

        _providers = go.GetComponents<IInteractionPrompt>();
        _interactables = go.GetComponents<IInteractable>();
        _availability = go.GetComponents<IInteractionAvailability>();
    }

    /// <summary>제공자가 하나도 없으면 이 GameObject엔 라벨을 붙일 이유가 없다.</summary>
    public bool HasAnyProvider => _providers.Length > 0 || _interactables.Length > 0;

    /// <summary>지금 띄울 문구를 계산한다. 띄울 게 없으면 false.</summary>
    public bool TryGet(out InteractionPromptInfo info)
    {
        // 1순위: 상황에 따라 문구가 바뀌는 제공자
        for (int i = 0; i < _providers.Length; i++)
        {
            info = _providers[i].GetInteractionPrompt();
            if (info.HasText) return true;
        }

        // 2순위: 고정 문구 IInteractable + 별도 가능여부 인터페이스
        for (int i = 0; i < _interactables.Length; i++)
        {
            string prompt = _interactables[i].InteractionPrompt;
            if (string.IsNullOrEmpty(prompt)) continue;

            info = new InteractionPromptInfo(null, prompt, IsAvailable());
            return true;
        }

        info = InteractionPromptInfo.None;
        return false;
    }

    /// <summary>IInteractionAvailability가 하나라도 false면 불가로 본다(없으면 항상 가능).</summary>
    private bool IsAvailable()
    {
        for (int i = 0; i < _availability.Length; i++)
        {
            if (!_availability[i].IsInteractionAvailable) return false;
        }
        return true;
    }
}
