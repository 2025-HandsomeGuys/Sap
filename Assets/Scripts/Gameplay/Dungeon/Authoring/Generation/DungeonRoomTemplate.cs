// @tags: dungeon, generation, room, template, data
namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 방 템플릿 1개. 격자는 [row, col] 인덱싱, 빈 칸은 '.'.
    /// 크기는 프리셋의 roomWidth/roomHeight와 정확히 일치해야 한다(파서가 검증).
    /// </summary>
    public class DungeonRoomTemplate
    {
        public RoomOpen Opens = RoomOpen.None;
        public int Weight = 1;

        public int Width;
        public int Height;
        public char[,] Tiles;   // [Height, Width]
        public char[,] Objects; // [Height, Width]
        public char[,] Links;   // [Height, Width] — LINKS 미존재 시 전부 '.'

        /// <summary>경고·에러 메시지용 표기. 예: "LR", "LRUD"</summary>
        public string Describe()
        {
            string opens = RoomTypeUtil.FormatOpens(Opens);
            return opens.Length > 0 ? opens : "(닫힌 방)";
        }
    }
}
