// @tags: audio, mixer, routing, component
using UnityEngine;

/// <summary>
/// 이 오브젝트(와 자식)의 AudioSource를 지정한 믹서 채널에 연결한다.
/// 스크립트가 붙어 있지 않은 씬/프리팹의 AudioSource를 설정 슬라이더 아래로 넣고 싶을 때,
/// 또는 자동 분류(SFX)와 다른 채널로 보내고 싶을 때 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class AudioChannelBinder : MonoBehaviour
{
    [Tooltip("이 오브젝트의 소리가 설정의 어느 슬라이더를 따를지.")]
    [SerializeField] private AudioChannel channel = AudioChannel.SFX;

    [Tooltip("인스펙터에서 이미 다른 믹서 그룹을 물려둔 소스까지 덮어쓸지.")]
    [SerializeField] private bool overwriteExisting = false;

    [Tooltip("자식 오브젝트의 AudioSource까지 연결할지.")]
    [SerializeField] private bool includeChildren = true;

    private void Awake() => Apply();

    /// <summary>런타임에 소스를 새로 붙였을 때 다시 부를 수 있다.</summary>
    public void Apply()
    {
        if (includeChildren) AudioRouting.RouteHierarchy(gameObject, channel, overwriteExisting);
        else                 AudioRouting.Route(GetComponent<AudioSource>(), channel, overwriteExisting);
    }
}
