// @tags: interface, terrain, modify, dig, chunk
using UnityEngine;

/// <summary>
/// 지형 수정(파괴, 생성 등)을 지원하는 객체 인터페이스
/// </summary>
public interface ITerrainModifiable
{
    /// <summary>
    /// 지정된 위치의 지형을 수정합니다 (채굴 등).
    /// </summary>
    /// <param name="worldPos">월드 좌표</param>
    /// <param name="radius">반경</param>
    /// <param name="toolIndex">사용 도구 인덱스</param>
    void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex);

    /// <summary>
    /// 마스크(스프라이트 등)를 사용하여 지형을 파냅니다.
    /// </summary>
    void Carve(Vector2 worldPos, Color32[] mask, int width, int height, Vector2 pivot);
}
