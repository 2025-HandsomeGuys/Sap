
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class TileBorderHighlighter : MonoBehaviour
{
    [Tooltip("명암을 적용할 타일맵")]
    public Tilemap targetTilemap;

    [Tooltip("테두리 가장 바깥쪽에 적용할 가장 진한 색상")]
    public Color borderColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    [Tooltip("그라데이션이 적용될 깊이 (타일 개수)")]
    public int gradientDepth = 4;

    [Tooltip("그라데이션이 끝나는 안쪽 타일의 색상")]
    public Color deepTileColor = Color.white;

    // 땅을 파거나 설치한 후에 이 함수를 호출하여 명암을 실시간으로 업데이트합니다.
    public void UpdateAllBorders()
    {
        /*
        if (targetTilemap == null) return;

        // 1. 타일맵의 모든 타일 위치를 가져옵니다.
        HashSet<Vector3Int> allTilePositions = new HashSet<Vector3Int>();
        foreach (var pos in targetTilemap.cellBounds.allPositionsWithin)
        {
            if (targetTilemap.HasTile(pos))
            {
                allTilePositions.Add(pos);
                // 우선 모든 타일의 색상과 플래그를 초기화
                targetTilemap.SetTileFlags(pos, TileFlags.None);
                targetTilemap.SetColor(pos, deepTileColor);
            }
        }

        // 2. 테두리 타일 찾기 (BFS 시작점)
        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        Dictionary<Vector3Int, int> distances = new Dictionary<Vector3Int, int>();

        foreach (var pos in allTilePositions)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (Mathf.Abs(x) == Mathf.Abs(y)) continue; // 대각선 제외

                    Vector3Int neighborPos = pos + new Vector3Int(x, y, 0);
                    if (!allTilePositions.Contains(neighborPos)) // 이웃이 빈 공간이면
                    {
                        distances[pos] = 1;
                        queue.Enqueue(pos);
                        goto nextTile;
                    }
                }
            }
            nextTile:;
        }

        // 3. BFS를 통해 안쪽으로 거리를 확장
        while (queue.Count > 0)
        {
            Vector3Int currentPos = queue.Dequeue();
            int currentDist = distances[currentPos];

            if (currentDist >= gradientDepth) continue;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (Mathf.Abs(x) == Mathf.Abs(y)) continue;

                    Vector3Int neighborPos = currentPos + new Vector3Int(x, y, 0);
                    if (allTilePositions.Contains(neighborPos) && !distances.ContainsKey(neighborPos))
                    {
                        distances[neighborPos] = currentDist + 1;
                        queue.Enqueue(neighborPos);
                    }
                }
            }
        }

        // 4. 계산된 거리에 따라 그라데이션 색상 적용
        foreach (var entry in distances)
        {
            float distance = entry.Value;
            float t = Mathf.Clamp01((distance - 1) / (gradientDepth - 1));
            Color color = Color.Lerp(borderColor, deepTileColor, t);
            targetTilemap.SetColor(entry.Key, color);
        }

        // Debug.Log("그라데이션 테두리 업데이트 완료."); // 너무 자주 호출되므로 주석 처리
        */
    }
}

