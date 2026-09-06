// @tags: sound, sfx, throttle, audio, pure
using System.Collections.Generic;

/// <summary>
/// 같은 키의 연타를 억제한다.
///
/// 한 프레임에 여러 번 발생하는 이벤트(다중 콜라이더 히트, 빠른 휠 스크롤)가
/// PlayOneShot으로 합산되면 소리가 터진다. MarketUISfx의 스크롤 스로틀과 같은 이유.
///
/// 시간을 주입받는 순수 C# — MonoBehaviour가 아니므로 EditMode에서 테스트한다.
///
/// 불변식: 억제된 호출은 타임스탬프를 갱신하지 않는다. 갱신하면 연타가 이어지는 동안
/// 창이 계속 밀려서 소리가 영원히 안 난다.
///
/// 키별로 독립이므로 서로 다른 소리는 같은 프레임에 겹쳐 울릴 수 있다(의도된 동작 —
/// 곡괭이 휘두르는 소리 위에 맞는 소리가 얹힌다).
/// </summary>
public sealed class SfxThrottle
{
    private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
    private readonly float _minInterval;

    public SfxThrottle(float minInterval = 0.04f)
    {
        _minInterval = minInterval;
    }

    /// <summary>재생해도 되면 true를 돌려주고 타임스탬프를 갱신한다.</summary>
    public bool ShouldPlay(string key, float now)
    {
        if (string.IsNullOrEmpty(key)) return false;

        if (_lastPlayed.TryGetValue(key, out float last) && now - last < _minInterval)
            return false; // 억제 — 타임스탬프는 갱신하지 않는다

        _lastPlayed[key] = now;
        return true;
    }

    public void Clear() => _lastPlayed.Clear();
}
