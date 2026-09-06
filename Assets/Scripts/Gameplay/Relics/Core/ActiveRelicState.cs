namespace Relic
{
    public enum RelicPhase { Ready, Active, Cooldown }

    public struct RelicTickResult
    {
        public bool  activeTick;    // 이 프레임 Active 지속 중
        public float activeElapsed; // 발동 후 경과(0..duration)
        public bool  activeEnded;   // 이 프레임 Active→Cooldown 전이
        public bool  becameReady;   // 이 프레임 Cooldown→Ready 전이
    }

    // 즉발(duration=0)과 지속형(duration>0)을 하나로 처리.
    // 순수 C# — MonoBehaviour 비의존(테스트 용이).
    public class ActiveRelicState
    {
        public RelicPhase Phase { get; private set; } = RelicPhase.Ready;
        public float Timer => _timer;

        private float _timer;
        private float _duration;
        private float _cooldown;

        // Ready일 때만 발동. duration/cooldown은 발동 시점 값으로 확정.
        public bool TryActivate(float duration, float cooldown)
        {
            if (Phase != RelicPhase.Ready) return false;

            _duration = duration;
            _cooldown = cooldown;

            if (duration > 0f)
            {
                Phase = RelicPhase.Active;
                _timer = 0f;
            }
            else
            {
                Phase = RelicPhase.Cooldown;
                _timer = cooldown;
            }
            return true;
        }

        // 지속형 액티브를 조기 종료(토글 off) → 바로 쿨타임 진입. Active 상태에서만 유효.
        public bool CancelToCooldown()
        {
            if (Phase != RelicPhase.Active) return false;
            Phase = RelicPhase.Cooldown;
            _timer = _cooldown;
            return true;
        }

        public RelicTickResult Tick(float dt)
        {
            var r = new RelicTickResult();
            switch (Phase)
            {
                case RelicPhase.Active:
                    _timer += dt;
                    if (_timer >= _duration)
                    {
                        Phase = RelicPhase.Cooldown;
                        _timer = _cooldown;
                        r.activeEnded = true;
                    }
                    else
                    {
                        r.activeTick = true;
                        r.activeElapsed = _timer;
                    }
                    break;

                case RelicPhase.Cooldown:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Phase = RelicPhase.Ready;
                        _timer = 0f;
                        r.becameReady = true;
                    }
                    break;
            }
            return r;
        }
    }
}
