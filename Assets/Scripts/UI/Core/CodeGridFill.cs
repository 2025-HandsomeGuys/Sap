// @tags: ui, grid, layout, code-generated, autofit, slot, warehouse, inventory

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <see cref="GridLayoutGroup"/>이 담긴 컨테이너의 **가로 폭을 남김없이 채우도록** 칸 수와 칸 크기를 자동 조정한다.
///
/// 코드 생성 오버레이(창고·인벤토리)는 패널 폭을 인스펙터/해상도로 바꿀 수 있는데,
/// 고정 칸 크기 격자는 넓어진 카드에서 오른쪽이 텅 비어 보인다(칸 수가 그대로라서).
/// 이 컴포넌트를 격자에 붙이면 실제 폭에 맞춰
///  - <see cref="targetCell"/> 근처 크기를 유지하며 들어갈 수 있는 만큼 칸 수를 잡고,
///  - 남는 자투리를 칸 크기에 고르게 나눠 폭을 딱 맞춘다(칸은 정사각 유지).
///
/// 폭이 바뀔 때(<see cref="OnRectTransformDimensionsChange"/>)마다 다시 계산하므로 해상도 변화에도 그대로 대응한다.
/// </summary>
[RequireComponent(typeof(GridLayoutGroup))]
[DisallowMultipleComponent]
public class CodeGridFill : MonoBehaviour
{
    [Tooltip("목표 칸 한 변(px). 이 크기에 가장 가깝게 칸 수를 정하고, 자투리는 칸 크기로 흡수한다.")]
    public float targetCell = 86f;

    [Tooltip("최소 칸 수 — 아주 좁아도 이 아래로는 안 내려간다.")]
    public int minColumns = 1;

    private GridLayoutGroup _grid;
    private RectTransform _rt;

    private void Awake() => Cache();
    private void OnEnable() { Cache(); Apply(); }
    private void OnRectTransformDimensionsChange() => Apply();

    private void Cache()
    {
        if (_grid == null) _grid = GetComponent<GridLayoutGroup>();
        if (_rt == null) _rt = (RectTransform)transform;
    }

    private void Apply()
    {
        Cache();
        if (_grid == null || _rt == null) return;

        float width = _rt.rect.width;
        if (width <= 1f) return; // 아직 레이아웃 전 — 다음 dimensions-change에서 다시 온다

        float inner = width - _grid.padding.left - _grid.padding.right;
        float sp = _grid.spacing.x;
        float t = Mathf.Max(8f, targetCell);

        // 목표 크기에 가장 가까운 칸 수(반올림) → 칸 크기가 targetCell 근처로 유지돼
        // 가방 등 다른 격자와 크게 어긋나지 않는다. 자투리는 칸 크기로 흡수해 폭을 정확히 채운다.
        int cols = Mathf.RoundToInt((inner + sp) / (t + sp));
        cols = Mathf.Max(Mathf.Max(1, minColumns), cols);

        float cell = Mathf.Floor((inner - sp * (cols - 1)) / cols);
        if (cell < 8f) return;

        // 값이 바뀔 때만 적용 — 불필요한 레이아웃 리빌드 방지.
        if (_grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount ||
            _grid.constraintCount != cols ||
            Mathf.Abs(_grid.cellSize.x - cell) > 0.5f ||
            Mathf.Abs(_grid.cellSize.y - cell) > 0.5f)
        {
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = cols;
            _grid.cellSize = new Vector2(cell, cell);
        }
    }
}
