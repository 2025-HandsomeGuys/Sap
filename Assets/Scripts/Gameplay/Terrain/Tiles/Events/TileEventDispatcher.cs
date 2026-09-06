// @tags: event, terrain, digging, listener, dispatcher, singleton
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// TerrainModifier.OnPixelDestroyed 이벤트를 수신하여
/// 등록된 ITileDestroyListener들에게 브로드캐스팅하는 싱글톤.
///
/// [SOLID]
///   SRP: 이벤트 브로드캐스팅만 담당. 픽셀 로직/리스너 비즈니스 로직은 각 구현체에 위임.
///   OCP: 새 리스너는 Register()만으로 확장 가능 — 이 클래스 수정 불필요.
///   DIP: 구체 클래스 대신 ITileDestroyListener 인터페이스에만 의존.
///
/// 씬의 Managers 오브젝트 아래에 배치한다.
/// </summary>
public class TileEventDispatcher : MonoBehaviour
{
    public static TileEventDispatcher Instance { get; private set; }

    private readonly List<ITileDestroyListener> _listeners = new List<ITileDestroyListener>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log("[TileEventDispatcher] 초기화 완료");
    }

    private void OnEnable()  => TerrainModifier.OnPixelDestroyed += Dispatch;
    private void OnDisable() => TerrainModifier.OnPixelDestroyed -= Dispatch;

    /// <summary>리스너 등록. 중복 등록 방지.</summary>
    public void Register(ITileDestroyListener listener)
    {
        if (!_listeners.Contains(listener))
        {
            _listeners.Add(listener);
            Debug.Log($"[TileEventDispatcher] 리스너 등록: {listener.GetType().Name} (총 {_listeners.Count}개)");
        }
    }

    /// <summary>리스너 해제.</summary>
    public void Unregister(ITileDestroyListener listener)
    {
        if (_listeners.Remove(listener))
            Debug.Log($"[TileEventDispatcher] 리스너 해제: {listener.GetType().Name} (총 {_listeners.Count}개)");
    }

    private void Dispatch(Vector2 worldPos, Color32 destroyedColor)
    {
        // 역방향 순회로 Unregister 도중 안전 보장
        for (int i = _listeners.Count - 1; i >= 0; i--)
            _listeners[i].OnTileDestroyed(worldPos, destroyedColor);
    }
}
