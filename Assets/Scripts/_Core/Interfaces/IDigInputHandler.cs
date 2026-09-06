// @tags: interface, input, dig, mining, player
using UnityEngine;

/// <summary>
/// 채굴 입력을 처리하는 인터페이스
/// </summary>
public interface IDigInputHandler
{
    /// <summary>
    /// 채굴 요청이 들어왔는지 확인합니다.
    /// </summary>
    bool IsDigRequested();

    /// <summary>
    /// 채굴할 목표 위치(월드 좌표)를 반환합니다.
    /// </summary>
    Vector2 GetTargetPosition();
}
