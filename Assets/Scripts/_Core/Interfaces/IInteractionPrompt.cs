// @tags: interface, interaction, prompt, localization, availability, text
/// <summary>
/// 근접 안내 문구 한 건. 문자열이 아니라 <b>Localization 키</b>를 들고 다니므로
/// 언어를 바꿔도 라벨이 알아서 다시 그린다.
///
/// <see cref="Available"/> 가 false면 "지금은 할 수 없다"는 안내 →
/// WorldInteractable의 문구 라벨이 차단 색(붉은 회색)으로 표시한다.
/// 예: 침대는 낮에 "낮에는 잘 수 없다"(false), 밤에 "하루를 마무리 하기"(true).
///
/// 문구가 상황에 따라 바뀌지 않는 오브젝트는 이 구조체를 쓸 필요 없이
/// <see cref="IInteractable.InteractionPrompt"/> 만 구현해도 라벨이 뜬다(항상 가능 색).
/// </summary>
public readonly struct InteractionPromptInfo
{
    /// <summary>Localization CSV의 key. 비어 있으면 <see cref="Fallback"/>을 그대로 쓴다.</summary>
    public readonly string Key;

    /// <summary>키를 못 찾았을 때 쓸 원문(개발 중 누락 대비).</summary>
    public readonly string Fallback;

    /// <summary>지금 상호작용이 가능한가. 문구 색을 가른다.</summary>
    public readonly bool Available;

    /// <summary>"{0}" 포맷 인자. 없으면 null.</summary>
    public readonly object[] Args;

    public InteractionPromptInfo(string key, string fallback, bool available, object[] args = null)
    {
        Key = key;
        Fallback = fallback;
        Available = available;
        Args = args;
    }

    /// <summary>키도 폴백도 없으면 "띄울 문구 없음" — 라벨이 숨는다.</summary>
    public bool HasText => !string.IsNullOrEmpty(Key) || !string.IsNullOrEmpty(Fallback);

    /// <summary>상호작용 가능 상태의 문구.</summary>
    public static InteractionPromptInfo Ok(string key, string fallback, params object[] args)
        => new InteractionPromptInfo(key, fallback, true, args);

    /// <summary>조건이 안 맞아 지금은 못 하는 상태의 문구(안내만 하고 색으로 구분).</summary>
    public static InteractionPromptInfo Blocked(string key, string fallback, params object[] args)
        => new InteractionPromptInfo(key, fallback, false, args);

    /// <summary>조건식으로 가능/불가를 한 줄에 정하는 형태.</summary>
    public static InteractionPromptInfo Of(bool available, string key, string fallback, params object[] args)
        => new InteractionPromptInfo(key, fallback, available, args);

    /// <summary>아무것도 띄우지 않음.</summary>
    public static InteractionPromptInfo None => default;
}

/// <summary>
/// ISP: 근접 시 띄울 문구를 "그때그때" 계산해 주는 선택적 인터페이스.
/// <see cref="IInteractable"/> 구현 여부와 무관하게 붙일 수 있다
/// (침대·마켓 단말기처럼 E 입력을 스스로 처리하는 오브젝트도 라벨을 띄우기 위함).
///
/// 매 프레임 호출되므로 할당 없이 가볍게 구현할 것.
/// </summary>
public interface IInteractionPrompt
{
    InteractionPromptInfo GetInteractionPrompt();
}
