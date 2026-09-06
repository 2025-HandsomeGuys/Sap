using UnityEngine;

namespace Relic
{
    /// <summary>XRay 단색화의 3톤 구분.</summary>
    public enum XRayTone
    {
        Terrain,     // 안 판 지형 — 중간 톤
        Background,  // 판 굴 너머 배경 — 가장 어두운 톤
        Object       // 광물·특수블록·함정 — 가장 밝은 톤
    }

    // XRay 3톤 단색화용 공유 머티리얼 묶음.
    // XRayController에서 분리한 이유: 머티리얼 주입·조회는 순수 로직이라
    // MonoBehaviour 밖에 두면 EditMode 테스트가 가능하고, 컨트롤러는
    // "무엇을 스왑할지"에만 집중할 수 있다.
    //
    // 여기 담기는 머티리얼은 sharedMaterial로 스왑되는 '공유' 에셋이다.
    // 인스턴스를 만들지 않으므로 렌더러 수와 무관하게 3개만 존재한다.
    public class XRayFlatPalette
    {
        private static readonly int FlatColorID = Shader.PropertyToID("_FlatColor");

        private readonly Material _terrain;
        private readonly Material _background;
        private readonly Material _object;

        public XRayFlatPalette(Material terrain, Material background, Material obj)
        {
            _terrain = terrain;
            _background = background;
            _object = obj;
        }

        public bool IsComplete => _terrain != null && _background != null && _object != null;

        /// <summary>3톤 색을 각 머티리얼의 _FlatColor에 주입한다. 미할당 머티리얼은 조용히 건너뛴다.</summary>
        public void Apply(Color terrain, Color background, Color obj)
        {
            if (_terrain != null) _terrain.SetColor(FlatColorID, terrain);
            if (_background != null) _background.SetColor(FlatColorID, background);
            if (_object != null) _object.SetColor(FlatColorID, obj);
        }

        /// <summary>스왑에 사용할 공유 머티리얼. 미할당이면 null(호출처가 스왑을 건너뛴다).</summary>
        public Material Get(XRayTone tone)
        {
            switch (tone)
            {
                case XRayTone.Terrain: return _terrain;
                case XRayTone.Background: return _background;
                case XRayTone.Object: return _object;
                default: return null;
            }
        }
    }
}
