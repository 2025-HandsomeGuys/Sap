// @tags: interaction, input, key, binding, world, single-source
using UnityEngine;

/// <summary>
/// 월드 상호작용 키의 <b>단일 원천</b>.
///
/// 인게임에서 오브젝트와 상호작용하는 키는 <b>F</b> 하나로 통일한다(구 E).
/// UI 안의 조작(이동 WASD · 탭 전환 Q/E · 확인 Space)은 여기와 <b>무관</b>하다 —
/// UI 키를 이 값으로 바꾸면 상호작용과 UI 확인이 같은 키가 되어 창이 열리자마자 눌린다.
///
/// 새로 키 입력을 받는 월드 오브젝트를 만들면 <c>KeyCode.F</c>를 직접 쓰지 말고
/// 반드시 <see cref="Interact"/>를 참조할 것 — 나중에 키를 바꿀 때 여기 한 줄만 고치면 된다.
/// </summary>
public static class InteractionKeys
{
    /// <summary>월드 상호작용 키.</summary>
    public const KeyCode Interact = KeyCode.F;

    /// <summary>화면에 글자로 보여줄 때 쓰는 이름(스프라이트가 없을 때의 폴백).</summary>
    public static string InteractLabel => Interact.ToString();

    /// <summary>이번 프레임에 상호작용 키가 눌렸는가.</summary>
    public static bool InteractPressed => Input.GetKeyDown(Interact);

    /// <summary>상호작용 키를 누르고 있는가(꾹 눌러 연속 줍기 등).</summary>
    public static bool InteractHeld => Input.GetKey(Interact);
}
