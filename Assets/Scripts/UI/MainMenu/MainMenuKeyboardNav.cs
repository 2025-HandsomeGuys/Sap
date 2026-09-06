// @tags: ui, mainmenu, keyboard, navigation, hover, highlight, wsad, spacebar

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 메인 메뉴 버튼(시작하기·이어하기·설정·게임종료) 키보드 조작.
///
/// W/S(또는 ↑/↓)로 줄 이동(꾹 눌러도 반복 없음·순환 없음), Space(또는 Enter)로 선택.
/// **선택된 버튼과 마우스를 올린 버튼은 텍스트 색이 노란색으로 바뀐다** — 둘은 같은 포커스를 공유한다.
///
/// ButtonGroup 오브젝트에 붙인다 — 자식 순서(위→아래)대로 <see cref="Button"/>을 자동 수집하므로
/// 인스펙터 연결 없이도 동작한다. <see cref="buttons"/>를 직접 지정하면 그 순서가 우선한다.
///
/// SaveSlot / Settings 오버레이가 열려 있는 동안에는 입력을 처리하지 않는다(각자 자체 조작이 있으므로).
/// 확인·선택 키는 프로젝트 규약대로 스페이스바로 통일한다.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuKeyboardNav : MonoBehaviour
{
    [Tooltip("선택/호버 시 버튼 텍스트 색 (기본: 노란색)")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Tooltip("비우면 자식 순서대로(위→아래) 자동 수집")]
    [SerializeField] private List<Button> buttons = new List<Button>();

    private readonly List<TMP_Text> _labels = new List<TMP_Text>();
    private readonly List<Color> _baseColors = new List<Color>();
    private int _selected = -1;

    private void Start()
    {
        Collect();
        // 첫 진입 선택 = 위에서부터 첫 상호작용 가능 버튼
        _selected = NextInteractable(0, +1);
        Apply();
    }

    private void Update()
    {
        if (_labels.Count == 0) return;
        // 오버레이가 떠 있으면 그쪽 조작에 양보
        if (SaveSlotUI.IsOpen || SettingsOverlayUI.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            Move(-1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            Move(+1);

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            Activate();
    }

    // ===================================================
    // 수집 / 하이라이트
    // ===================================================
    private void Collect()
    {
        if (buttons.Count == 0)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var b = transform.GetChild(i).GetComponent<Button>();
                if (b != null) buttons.Add(b);
            }
        }

        _labels.Clear();
        _baseColors.Clear();
        for (int i = 0; i < buttons.Count; i++)
        {
            var label = buttons[i] != null ? buttons[i].GetComponentInChildren<TMP_Text>(true) : null;
            _labels.Add(label);
            _baseColors.Add(label != null ? label.color : Color.white);
            AddHover(buttons[i], i);
        }
    }

    // 마우스 호버도 키보드 선택과 같은 포커스를 공유하게 한다.
    private void AddHover(Button b, int index)
    {
        if (b == null) return;
        var trigger = b.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = b.gameObject.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (b.interactable) SetSelected(index); });
        trigger.triggers.Add(enter);
    }

    private void Move(int dir)
    {
        int from = _selected < 0 ? (dir > 0 ? -1 : buttons.Count) : _selected;
        int next = NextInteractable(from + dir, dir);
        if (next >= 0) SetSelected(next);
    }

    // idx부터 dir 방향으로 상호작용 가능한 첫 버튼 인덱스. 없으면 -1 (순환 없음).
    private int NextInteractable(int idx, int dir)
    {
        for (int i = idx; i >= 0 && i < buttons.Count; i += dir)
        {
            var b = buttons[i];
            if (b != null && b.interactable && b.gameObject.activeInHierarchy) return i;
        }
        return -1;
    }

    private void SetSelected(int index)
    {
        if (index == _selected) return;
        _selected = index;
        Apply();
    }

    private void Apply()
    {
        for (int i = 0; i < _labels.Count; i++)
        {
            if (_labels[i] == null) continue;
            _labels[i].color = (i == _selected) ? highlightColor : _baseColors[i];
        }
    }

    private void Activate()
    {
        if (_selected < 0 || _selected >= buttons.Count) return;
        var b = buttons[_selected];
        if (b != null && b.interactable) b.onClick.Invoke();
    }
}
