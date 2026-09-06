// @tags: ui, slot, navigation, keyboard, wasd, focus, inventory, warehouse, button, code-generated

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드 생성 UI를 WASD로 돌아다니게 해 주는 키보드 내비게이터.
/// 창고 오버레이와 지하 인벤토리 오버레이가 공용으로 쓴다.
///
/// 대상은 슬롯(<see cref="CodeSlotView"/>)과 버튼(<see cref="CodeNavButton"/>) — 둘 다
/// <see cref="ICodeNavItem"/>이라 **한 판 위에 섞어서** 다룬다. 격자가 여러 개(창고 격자 ·
/// 장비 · 아이템 · 유물)이고 버튼은 격자 밖에 있으므로, '몇 번째 칸'이 아니라
/// **화면 좌표 기준 방향 탐색**을 한다 — 소유자는 <see cref="collect"/>로 항목만 넘기면
/// 격자 경계를 넘는 이동(창고 오른쪽 끝 → 가방, 격자 아래 → 정렬 버튼)이 자동으로 된다.
///
/// 포커스 표시는 마우스 호버와 같은 강조를 쓰고, 마우스를 올리면 그 항목으로 커서가
/// 따라간다(<see cref="CodeNav.HoverEntered"/>) — 두 입력이 커서 하나를 공유한다.
/// </summary>
public class CodeSlotNavigator
{
    /// <summary>지금 조작 가능한 항목을 채운다(소유자가 채움). 꺼진 항목은 내비게이터가 알아서 거른다.</summary>
    public System.Action<List<ICodeNavItem>> collect;

    /// <summary>
    /// 스페이스바 처리를 소유자가 가로챌 때 사용(구역별로 다르게 동작해야 하는 경우).
    /// 비우면 항목 자신의 <see cref="ICodeNavItem.NavActivate"/>가 호출된다.
    /// </summary>
    public System.Action<ICodeNavItem> onActivate;

    /// <summary>커서가 움직일 때 낼 효과음 이름(비우면 무음).</summary>
    public string moveSfxName;

    /// <summary>
    /// 그 방향에 더 갈 항목이 없을 때(끝 줄) 호출. 구역 밖으로 빠져나가는 처리에 쓴다
    /// (예: 필터 패널 왼쪽 끝에서 A → 상단 툴바로 복귀). 비우면 제자리에 머문다.
    /// </summary>
    public System.Action<Vector2> onEdge;

    /// <summary>
    /// 켜면 '같은 줄' 항목만 이동 대상으로 삼는다(대각선·다른 줄로 새지 않음). 줄 끝에서는
    /// 후보 없음 → <see cref="onEdge"/>가 불린다. 좌우 이동이 옆 줄로 튀면 안 되는 격자(필터 패널)에서 쓴다.
    /// 창고처럼 '격자 오른쪽 끝 → 옆 버튼'식 다른 줄 점프가 필요한 곳은 끈 채로 둔다(기본).
    /// </summary>
    public bool inLineOnly;

    // 꾹 누르고 있을 때의 반복 간격 — SettingsOverlayUI의 W/S 이동과 같은 감각으로 맞춘다.
    private const float RepeatDelay = 0.38f;
    private const float RepeatInterval = 0.07f;

    // 방향 점수 = 진행 거리 + 옆으로 벗어난 거리 × 이 값.
    // 크게 줄수록 '똑바로 위/아래/옆'을 강하게 선호한다(대각선 항목으로 새지 않게).
    private const float SideWeight = 3f;

    // '같은 줄'로 인정할 최소 겹침(px). 살짝 스치는 정도는 다른 줄로 본다.
    private const float LineOverlap = 2f;

    private static readonly KeyCode[] DirKeys = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D };
    private static readonly Vector3[] Corners = new Vector3[4];

    private readonly List<ICodeNavItem> _buf = new List<ICodeNavItem>();
    private ICodeNavItem _focus;
    private KeyCode _heldKey = KeyCode.None;
    private float _nextRepeat;
    private bool _active;
    private int _beginFrame = -1; // Begin()이 불린 프레임 — 그 프레임의 키 입력은 무시(진입시킨 키가 즉시 실행되는 것 방지)

    // 목록이 다시 그려지며 포커스 항목이 꺼졌을 때, 커서를 '그 자리'로 되돌리기 위한 마지막 위치.
    private Vector2 _lastCenter;
    private bool _hadFocus;

    /// <summary>현재 포커스된 항목 (없거나 꺼졌으면 null).</summary>
    public ICodeNavItem Focused => IsUsable(_focus) ? _focus : null;

    // ===================================================
    // 수명
    // ===================================================
    /// <summary>오버레이가 열릴 때. 마우스 호버와 포커스를 연동한다.</summary>
    public void Begin()
    {
        if (_active) return;
        _active = true;
        _heldKey = KeyCode.None;
        _beginFrame = Time.frameCount; // 이 프레임의 입력(진입시킨 키)은 이번 Update에서 건너뛴다
        CodeNav.HoverEntered += OnItemHovered;
    }

    /// <summary>오버레이가 닫힐 때. 포커스 강조를 걷어낸다.</summary>
    public void End()
    {
        if (!_active) return;
        _active = false;
        CodeNav.HoverEntered -= OnItemHovered;
        SetFocus(null, false);
        _hadFocus = false;
        _heldKey = KeyCode.None;
    }

    /// <summary>포커스만 해제.</summary>
    public void ClearFocus()
    {
        SetFocus(null, false);
        _hadFocus = false;
    }

    /// <summary>
    /// 커서를 특정 항목으로 직접 옮긴다 — 소유자가 구역 경계 점프(예: 목록 끝 → 옆 패널 버튼)를
    /// 좌표 탐색 대신 확정적으로 지정하고 싶을 때. 꺼진 항목이면 무시한다.
    /// </summary>
    public void FocusItem(ICodeNavItem item, bool scrollIntoView = true)
    {
        if (!_active) return;
        if (item != null && !IsUsable(item)) return;
        SetFocus(item, scrollIntoView);
    }

    /// <summary>
    /// 목록을 다시 그린 뒤 호출. 포커스 항목이 꺼졌으면(마지막 물건을 꺼내 칸이 사라진 경우 등)
    /// 원래 있던 자리에서 가장 가까운 항목으로 커서를 옮긴다 — 커서가 통째로 사라지지 않게.
    /// </summary>
    public void Refresh()
    {
        if (!_active || IsUsable(_focus)) return;

        SetFocus(null, false);
        if (!_hadFocus) return;

        CollectUsable();
        ICodeNavItem nearest = null;
        float best = float.MaxValue;
        foreach (var item in _buf)
        {
            float d = (CenterOf(item) - _lastCenter).sqrMagnitude;
            if (d < best) { best = d; nearest = item; }
        }

        if (nearest != null) SetFocus(nearest, false);
        else _hadFocus = false;
    }

    // ===================================================
    // 입력
    // ===================================================
    /// <summary>오버레이의 Update에서 매 프레임 호출.</summary>
    public void Update()
    {
        if (!_active) return;
        if (Time.frameCount == _beginFrame) return; // 진입한 프레임의 키(WASD·Space)는 소비하지 않는다

        KeyCode key = ReadDirKey();
        if (key == KeyCode.None)
        {
            _heldKey = KeyCode.None;
        }
        else
        {
            bool fire;
            if (key != _heldKey)
            {
                _heldKey = key;
                _nextRepeat = Time.unscaledTime + RepeatDelay;
                fire = true;
            }
            else
            {
                fire = Time.unscaledTime >= _nextRepeat;
                if (fire) _nextRepeat = Time.unscaledTime + RepeatInterval;
            }

            if (fire) Move(DirOf(key));
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            var focused = Focused;
            if (focused == null) return;

            if (onActivate != null) onActivate(focused);
            else focused.NavActivate();
        }
    }

    /// <summary>새로 누른 키가 우선. 없으면 계속 누르고 있는 키를 유지한다.</summary>
    private KeyCode ReadDirKey()
    {
        for (int i = 0; i < DirKeys.Length; i++)
            if (Input.GetKeyDown(DirKeys[i])) return DirKeys[i];

        if (_heldKey != KeyCode.None && Input.GetKey(_heldKey)) return _heldKey;

        for (int i = 0; i < DirKeys.Length; i++)
            if (Input.GetKey(DirKeys[i])) return DirKeys[i];

        return KeyCode.None;
    }

    private static Vector2 DirOf(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.W: return Vector2.up;
            case KeyCode.S: return Vector2.down;
            case KeyCode.A: return Vector2.left;
            default: return Vector2.right;
        }
    }

    // ===================================================
    // 이동
    // ===================================================
    /// <summary>포커스를 dir 방향의 가장 가까운 항목으로 옮긴다. 포커스가 없으면 첫 항목으로 진입만 한다.</summary>
    public void Move(Vector2 dir)
    {
        CollectUsable();
        if (_buf.Count == 0) { SetFocus(null, false); return; }

        var current = Focused;
        if (current == null || !_buf.Contains(current))
        {
            // 첫 입력은 '커서 켜기'만 — 바로 한 칸 움직이면 어디서 시작했는지 알 수 없다.
            SetFocus(_buf[0], true);
            return;
        }

        Vector2 from = CenterOf(current);
        SpanOf(current.NavRect, dir, out float fromLo, out float fromHi);

        // 같은 줄(=이동 방향과 직각으로 겹치는) 항목이 먼저다. 겹치는 항목이 하나도 없을 때만
        // 다른 줄로 넘어간다 — 안 그러면 '메인' 탭에서 D를 눌렀을 때 옆 칸(서브 I)이 아니라
        // 살짝 위에 있는 윗줄 탭(퀘스트)으로 새 버린다(가깝다는 이유만으로 이긴다).
        ICodeNavItem bestInLine = null, bestOff = null;
        float bestInLineScore = float.MaxValue, bestOffScore = float.MaxValue;

        foreach (var item in _buf)
        {
            if (ReferenceEquals(item, current)) continue;

            Vector2 delta = CenterOf(item) - from;
            float along = delta.x * dir.x + delta.y * dir.y;
            if (along <= 1f) continue; // 진행 방향 반대편·같은 줄은 후보가 아니다

            float side = Mathf.Abs(delta.x * dir.y - delta.y * dir.x);
            float score = along + side * SideWeight;

            SpanOf(item.NavRect, dir, out float lo, out float hi);
            bool inLine = Mathf.Min(hi, fromHi) - Mathf.Max(lo, fromLo) > LineOverlap;

            if (inLine) { if (score < bestInLineScore) { bestInLineScore = score; bestInLine = item; } }
            else if (score < bestOffScore) { bestOffScore = score; bestOff = item; }
        }

        // inLineOnly면 같은 줄 후보만 인정 → 줄 끝에서 옆 줄로 새지 않고 곧장 onEdge로 빠진다.
        var best = inLineOnly ? bestInLine : (bestInLine ?? bestOff);
        if (best == null) { onEdge?.Invoke(dir); return; } // 끝 줄 — 순환 대신 소유자에게 알림(없으면 제자리)
        SetFocus(best, true);
        if (!string.IsNullOrEmpty(moveSfxName)) CodeUI.PlaySfx(moveSfxName);
    }

    /// <summary>소유자에게서 현재 항목을 받아 꺼진 것을 걸러 _buf에 남긴다.</summary>
    private void CollectUsable()
    {
        _buf.Clear();
        collect?.Invoke(_buf);
        for (int i = _buf.Count - 1; i >= 0; i--)
            if (!IsUsable(_buf[i])) _buf.RemoveAt(i);
    }

    private void OnItemHovered(ICodeNavItem item)
    {
        if (!_active || !IsUsable(item)) return;
        SetFocus(item, false); // 마우스가 이미 그 위에 있으므로 스크롤은 건드리지 않는다
    }

    private void SetFocus(ICodeNavItem item, bool scrollIntoView)
    {
        if (ReferenceEquals(_focus, item))
        {
            if (scrollIntoView && item != null) EnsureVisible(item);
            return;
        }

        if (_focus != null) _focus.SetNavFocus(false);
        _focus = item;
        if (_focus == null) return;

        _focus.SetNavFocus(true);
        _lastCenter = CenterOf(_focus);
        _hadFocus = true;
        if (scrollIntoView) EnsureVisible(_focus);
    }

    private static bool IsUsable(ICodeNavItem item) => item != null && item.NavUsable;

    /// <summary>
    /// 이동 방향과 <b>직각</b>인 축에서 항목이 차지하는 구간(화면 px).
    /// 좌우 이동이면 y 구간, 상하 이동이면 x 구간 — 이 구간이 겹치면 '같은 줄'로 본다.
    /// </summary>
    private static void SpanOf(RectTransform rt, Vector2 dir, out float lo, out float hi)
    {
        rt.GetWorldCorners(Corners);
        bool horizontal = Mathf.Abs(dir.x) > 0.5f;

        lo = float.MaxValue;
        hi = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float v = horizontal ? Corners[i].y : Corners[i].x;
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
    }

    private static Vector2 CenterOf(ICodeNavItem item)
    {
        var rt = item.NavRect;
        return rt.TransformPoint(rt.rect.center); // 스크린 스페이스 오버레이 캔버스 = 월드 좌표가 곧 화면 px
    }

    /// <summary>포커스 항목이 스크롤 목록 밖에 있으면 보이는 자리까지 목록을 민다.</summary>
    private static void EnsureVisible(ICodeNavItem item)
    {
        var rect = item.NavRect;
        var scroll = rect.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;

        var viewport = scroll.viewport;
        rect.GetWorldCorners(Corners);

        float top = float.MinValue, bottom = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            float y = viewport.InverseTransformPoint(Corners[i]).y;
            if (y > top) top = y;
            if (y < bottom) bottom = y;
        }

        var vp = viewport.rect;
        const float pad = 6f;

        float dy = 0f;
        if (top > vp.yMax - pad) dy = top - (vp.yMax - pad);
        else if (bottom < vp.yMin + pad) dy = bottom - (vp.yMin + pad);
        if (Mathf.Approximately(dy, 0f)) return;

        // content는 위쪽 피벗 — y를 올리면 내용이 위로 간다. 항목을 dy만큼 내려야 하므로 그만큼 뺀다.
        scroll.content.anchoredPosition -= new Vector2(0f, dy);
    }
}
