// @tags: dungeon, save, data-container, dto
using System.Collections.Generic;

[System.Serializable]
public class DungeonSaveData
{
    public List<DungeonInstanceEntry> entries = new List<DungeonInstanceEntry>();
}

[System.Serializable]
public class DungeonInstanceEntry
{
    public int x;   // 문 청크 좌표 X
    public int y;   // 문 청크 좌표 Y
    public List<string> collectedRewardIds = new List<string>(); // 수집한 보상 ID
    public List<int>    brokenRockIds      = new List<int>();     // 채굴로 부서진 rock ID
    public bool         used;                                     // 한 번 탐험(진입→이탈) 완료 → 재입장 불가
}
