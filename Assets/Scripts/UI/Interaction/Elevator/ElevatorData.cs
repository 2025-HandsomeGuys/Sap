using System.Collections.Generic;
using UnityEngine;

// [엘리베이터 정류장 정보 클래스]
// 지층 하나당 상층·중층·하층 3개가 만들어진다 (ElevatorLayerCatalog.Build 참고).
public class LayerInfo
{
    public int layerIndex;         // 정류장 인덱스. ElevatorUI 버튼 순서와 1:1 대응
    public TileType tileType;      // 해당 정류장이 속한 지층의 타일 타입 (상·중·하층이 공유)
    public int startDepth;         // 이 정류장의 청크 Y 좌표. ElevatorLayerCatalog가 지층 startDepth + 오프셋으로 계산
    public string layerName;       // 표시 이름 (UI 표시용, 예: "얼음땅 하층")

    public LayerInfo(int index, TileType type, int depth, string name)
    {
        layerIndex = index;
        tileType = type;
        startDepth = depth;
        layerName = name;
    }
}

// [엘리베이터 정보 클래스]
public class ElevatorInfo
{
    public int xChunk;             // 청크 X 좌표
    public int yChunk;             // 청크 Y 좌표
    public int layerIndex;         // 지층 인덱스 (0~6)
    public Vector2 worldPosition;  // 월드 좌표
    public ElevatorController controller; // 엘리베이터 컨트롤러 참조

    public ElevatorInfo(int x, int y, int layer, Vector2 pos)
    {
        xChunk = x;
        yChunk = y;
        layerIndex = layer;
        worldPosition = pos;
        controller = null;
    }
}
