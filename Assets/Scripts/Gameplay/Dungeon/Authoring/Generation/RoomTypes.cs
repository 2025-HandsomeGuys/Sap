// @tags: dungeon, generation, room, type, enum
using System;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>방의 열린 면(개구부) 집합. 미로 배선에서 인접 방과 이어지는 방향.</summary>
    [Flags]
    public enum RoomOpen
    {
        None = 0,
        L = 1,
        R = 2,
        U = 4,
        D = 8,
    }

    /// <summary>
    /// 격자 한 칸의 역할. Start/End는 E·X가 놓일 자리 표시일 뿐 템플릿 선택에는 쓰지 않는다.
    /// Rock은 방이 아니라 통짜 암반 — 템플릿을 찍지 않고 벽으로 남긴다.
    /// </summary>
    public enum RoomRole
    {
        Normal,
        Start,
        End,
        Rock,
    }

    public static class RoomTypeUtil
    {
        /// <summary>"LR", " l d " 같은 문자열을 플래그로. 대소문자·공백 무시. 빈 문자열은 None.</summary>
        public static bool TryParseOpens(string s, out RoomOpen opens)
        {
            opens = RoomOpen.None;
            if (s == null) return false;

            foreach (char raw in s)
            {
                if (char.IsWhiteSpace(raw)) continue;
                switch (char.ToUpperInvariant(raw))
                {
                    case 'L': opens |= RoomOpen.L; break;
                    case 'R': opens |= RoomOpen.R; break;
                    case 'U': opens |= RoomOpen.U; break;
                    case 'D': opens |= RoomOpen.D; break;
                    default: opens = RoomOpen.None; return false;
                }
            }
            return true;
        }

        /// <summary>플래그를 항상 L→R→U→D 순의 문자열로. 경고 메시지·비교에 쓴다.</summary>
        public static string FormatOpens(RoomOpen opens)
        {
            var sb = new System.Text.StringBuilder(4);
            if ((opens & RoomOpen.L) != 0) sb.Append('L');
            if ((opens & RoomOpen.R) != 0) sb.Append('R');
            if ((opens & RoomOpen.U) != 0) sb.Append('U');
            if ((opens & RoomOpen.D) != 0) sb.Append('D');
            return sb.ToString();
        }

        /// <summary>단일 방향의 반대. 경로가 A→B로 이동할 때 B에 뚫을 면을 구한다.</summary>
        public static RoomOpen Opposite(RoomOpen dir)
        {
            switch (dir)
            {
                case RoomOpen.L: return RoomOpen.R;
                case RoomOpen.R: return RoomOpen.L;
                case RoomOpen.U: return RoomOpen.D;
                case RoomOpen.D: return RoomOpen.U;
                default: return RoomOpen.None;
            }
        }
    }
}
