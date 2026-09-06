// @tags: save, slot, name, sanitize, pure-logic

using System.Text;

/// <summary>
/// 세이브 슬롯 표시 이름 정규화 규칙.
///
/// UI(SaveSlotUI)와 저장 경로(SaveManager) 양쪽이 같은 규칙을 쓰도록 한 곳에 모았다.
/// 입력창에서 한 번 다듬고 저장 직전에 또 다듬어도 결과가 같아야 하므로 멱등(idempotent)이다.
/// Unity 타입에 의존하지 않는 순수 로직이라 EditMode 테스트로 검증한다.
/// </summary>
public static class SaveSlotName
{
    /// <summary>저장되는 이름의 최대 글자 수. 슬롯 한 줄에 들어가는 폭 기준.</summary>
    public const int MaxLength = 16;

    /// <summary>
    /// 사용자 입력을 저장 가능한 이름으로 다듬는다.
    ///
    /// - 개행·탭·제어문자는 공백으로 취급 (JSON 한 줄 표시가 깨지지 않게)
    /// - 앞뒤 공백 제거, 연속 공백은 하나로 축약
    /// - 축약 후 <see cref="MaxLength"/>를 넘는 부분은 자른다
    /// - 결과가 비면 null — "이름 없음"을 뜻하고 UI가 "슬롯 N"으로 폴백한다
    /// </summary>
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        var sb = new StringBuilder(raw.Length);
        bool pendingSpace = false;

        foreach (char c in raw)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                // 선행 공백은 버린다. 그 외에는 '다음 글자가 실제로 오면' 그때 한 칸 넣는다
                // → 연속 공백 축약과 후행 공백 제거가 동시에 처리된다.
                if (sb.Length > 0) pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                if (sb.Length >= MaxLength) break;
                sb.Append(' ');
                pendingSpace = false;
            }

            if (sb.Length >= MaxLength) break;
            sb.Append(c);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>정규화 후에도 남는 이름이 있는지. (빈 입력을 기본 이름으로 되돌릴지 판단용)</summary>
    public static bool HasName(string raw) => Sanitize(raw) != null;
}
