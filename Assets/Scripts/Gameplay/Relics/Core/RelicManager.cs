using System.Collections.Generic;
using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 플레이어에 부착. 로드아웃·훅중계·액티브 상태기계·스폰물 수명 총괄.
    public class RelicManager : MonoBehaviour
    {
        /// <summary>
        /// 현재 씬의 매니저. Awake에서 잡고 OnDestroy에서 놓는다.
        /// 돌을 깰 때마다 유물 드롭을 판정하는 <c>RelicDropRoller</c>가 매번
        /// FindFirstObjectByType을 돌지 않게 하려고 둔 캐시다(씬당 1개 전제).
        /// </summary>
        public static RelicManager Instance { get; private set; }

        /// <summary>업그레이드 없이 주어지는 유물 칸 수 = <b>0</b>.
        /// 유물 칸은 전부 업그레이드로 열린다 — <see cref="UpgradeEffectType.RelicSlotUp"/> 노드가
        /// 여기에 더한다(현재 트리: RelicSlot_T0_01, RelicSlot_T1_02 → 최대 2칸).
        ///
        /// ⚠ 0으로 두면 노드를 사기 전에는 <b>슬롯 배열이 길이 0</b>이다. 슬롯을 만지는 코드는
        /// 전부 범위 검사를 통과해야 한다(ActivateSlot·EquipSlot·GetSlotPhase는 이미 가드가 있다).
        /// 유물을 얻어도 칸이 없으면 보유만 되고 장착은 안 된다 — 의도된 동작이다.</summary>
        public const int BaseSlotCount = 0;

        /// <summary>UI가 '잠긴 칸'까지 그려 줄 때 쓰는 최대 칸 수(업그레이드 트리의 RelicSlotUp 총합).
        /// 실제 사용 가능한 칸은 <see cref="RelicInventory.SlotCount"/>다.</summary>
        public const int MaxSlotCount = 2;

        public RelicInventory Inventory { get; private set; } = new RelicInventory();

        private RelicContext _ctx;
        private readonly RelicStatProvider _statProvider = new RelicStatProvider();
        private PlayerStat _playerStat;
        private IPlayerController _controller;

        // 슬롯별 런타임 behavior(clone) + 액티브 상태
        private RelicBehaviour[] _slotBehaviour;
        private ActiveRelicState[] _slotActive;

        // 유물별 스폰물 추적
        private readonly Dictionary<RelicID, List<GameObject>> _spawned = new Dictionary<RelicID, List<GameObject>>();
        private RelicID[] _spawnOwnerBySlot; // slot -> 현재 장착 유물 id (스폰 소유자 매핑)

        /// <summary>
        /// 씬에 RelicManager가 없으면 플레이어 오브젝트에 런타임 부착한다.
        /// 원래는 플레이어 프리팹에 있어야 하지만 지금은 DemoUnderground 씬에만 씬-추가돼 있어,
        /// 상점이 있는 지상 씬처럼 매니저가 없는 씬에서도 지급·저장이 되도록 하는 안전망.
        /// 이미 있으면 그대로 반환하므로 중복 부착은 생기지 않는다.
        /// 플레이어가 없는 씬(메인메뉴 등)에서는 만들지 않고 null을 반환한다.
        /// </summary>
        public static RelicManager EnsureInScene()
        {
            if (Instance != null) return Instance;

            var mgr = FindFirstObjectByType<RelicManager>(FindObjectsInactive.Include);
            if (mgr != null) return mgr;

            // 부착 대상 = 플레이어 루트 (DemoUnderground의 배치와 동일하게)
            Component host = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (host == null) host = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
            if (host == null) return null;

            mgr = host.gameObject.AddComponent<RelicManager>(); // Awake 즉시 실행 → ctx 구성 완료

            // 세이브에 보유 유물이 있으면 곧바로 복원한다.
            // 빈 채로 두면 다음 SaveManager.Save()가 relicSave를 빈 데이터로 덮어쓴다.
            var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
            var saved = (sm != null && sm.playerData != null) ? sm.playerData.relicSave : null;
            if (saved != null && saved.hasData) mgr.ApplySaveData(saved);

            Debug.Log("[RelicManager] 씬에 매니저가 없어 플레이어에 런타임 부착했습니다.");
            return mgr;
        }

        private void Awake()
        {
            Instance = this;

            _playerStat = GetComponentInParent<PlayerStat>();
            if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>();

            _ctx = new RelicContext
            {
                player = transform,
                stat   = _playerStat,
                mining = GetComponentInParent<PlayerMining>() ?? FindFirstObjectByType<PlayerMining>(),
                tools  = GetComponentInParent<ToolController>() ?? FindFirstObjectByType<ToolController>(),
                statProvider = _statProvider,
                runner = this,
                terrain = ResolveTerrainManager()
            };

            _statProvider.OnChanged = () => { if (_playerStat != null) _playerStat.MarkDirty(); };
            if (_playerStat != null) _playerStat.RegisterProvider(_statProvider);

            int n = Inventory.SlotCount;
            _slotBehaviour = new RelicBehaviour[n];
            _slotActive = new ActiveRelicState[n];
            _spawnOwnerBySlot = new RelicID[n];

            _controller = GetComponentInParent<PlayerController>();
            if (_controller == null) _controller = FindFirstObjectByType<PlayerController>();
            if (_controller != null)
            {
                _ctx.controller = _controller;
                _controller.AirJumpQuery = TryConsumeAirJump;
                _controller.GroundJumpQuery = AllowGroundJump;
                _controller.Landed += NotifyLanded;
            }

            // 파기 스윙 허브를 한 번만 구독해 전 슬롯으로 fan-out(개별 유물 구독 제거).
            if (_ctx.mining != null)
                _ctx.mining.DigSwing += NotifyDigSwing;
        }

        private void OnEnable()
        {
            // 업그레이드로 지급되는 유물(RelicSO.unlockNodeId)과 늘어난 슬롯 수를 따라잡는다.
            // Awake가 아니라 여기인 이유: 세이브 로드(ApplySaveData)가 Inventory를 통째로
            // 갈아끼우므로, 로드 뒤에도 한 번 더 훑어야 지급이 남는다(ApplySaveData 끝에서도 호출).
            var um = UpgradeManager.Instance;
            if (um != null) um.OnUpgradeStateChanged += SyncUpgrades;
            SyncUpgrades();
        }

        private void OnDisable()
        {
            var um = UpgradeManager.Instance;
            if (um != null) um.OnUpgradeStateChanged -= SyncUpgrades;
        }

        /// <summary>
        /// 업그레이드를 사면 따라잡아야 하는 것 전부. <b>슬롯을 먼저 늘리는 순서가 중요하다</b> —
        /// 반대로 하면 같은 프레임에 지급된 유물이 빈 칸을 못 찾아 자동 장착에서 밀린다.
        /// </summary>
        private void SyncUpgrades()
        {
            SyncUpgradeSlotCount();
            SyncUpgradeUnlockedRelics();
        }

        /// <summary>
        /// `unlockNodeId`가 걸린 유물 중 그 노드를 이미 산 것을 보유로 넣는다.
        /// 도구(toolConfig.json)·시설(WorldInteractable)이 nodeId 문자열로 해금을 거는 것과 같은 방식이다 —
        /// 업그레이드 쪽에 유물 지식을 심지 않으려고 소비자(여기)에서 대조한다.
        /// 여러 번 불려도 Grant가 이미 보유한 유물을 무시하므로 안전하다.
        /// </summary>
        public void SyncUpgradeUnlockedRelics()
        {
            var db = RelicDatabase.Instance;
            var um = UpgradeManager.Instance;
            if (db == null || db.allRelics == null || um == null) return;

            for (int i = 0; i < db.allRelics.Count; i++)
            {
                var so = db.allRelics[i];
                if (so == null || string.IsNullOrEmpty(so.unlockNodeId)) continue;
                if (Inventory.IsOwned(so.id)) continue;
                if (!um.IsNodeUnlocked(so.unlockNodeId)) continue;

                GrantAndAutoEquip(so.id);

                Debug.Log($"[RelicManager] 업그레이드 해금으로 유물 지급: {so.id} (노드 {so.unlockNodeId})");
            }
        }

        /// <summary>
        /// 업그레이드로 늘어난 유물 칸 수를 반영한다.
        ///
        /// 슬롯 수는 세이브(<c>RelicSaveData.slotCount</c>)에도 실리지만 <b>원본은 업그레이드</b>다.
        /// 세이브를 원본으로 두면 노드를 사기 전에 만들어진 파일에서 칸이 도로 줄어든다.
        ///
        /// 칸이 <b>줄어들 수도</b> 있다(뉴게임·다른 슬롯 세이브 → 노드 미보유 → 0칸). 줄일 때는
        /// 반드시 밀려날 칸을 먼저 <see cref="UnequipSlot"/>로 내린다 — 그냥 배열만 줄이면
        /// 장착돼 있던 유물의 <c>OnUnequip</c>을 아무도 못 불러 효과와 스폰물(드론·빔)이 그대로 남는다.
        /// </summary>
        public void SyncUpgradeSlotCount()
        {
            var um = UpgradeManager.Instance;
            if (um == null) return;

            int want = Mathf.RoundToInt(um.GetStatValue(UpgradeEffectType.RelicSlotUp, BaseSlotCount));
            want = Mathf.Max(BaseSlotCount, want);
            if (want == Inventory.SlotCount) return;

            // 줄어드는 경우: 사라질 칸을 먼저 정리(behavior OnUnequip + 스폰물 회수).
            if (want < Inventory.SlotCount && _slotBehaviour != null)
                for (int i = want; i < Inventory.SlotCount; i++)
                    UnequipSlot(i);

            Inventory.SetSlotCount(want);
            ResizeSlotArrays(want);

            Debug.Log($"[RelicManager] 업그레이드로 유물 슬롯 {want}칸");
        }

        // 슬롯 배열 3종을 같은 길이로 맞춘다. 기존 칸의 behavior·액티브 상태·스폰 소유자는 그대로 남는다.
        private void ResizeSlotArrays(int n)
        {
            if (_slotBehaviour != null && _slotBehaviour.Length == n) return;
            System.Array.Resize(ref _slotBehaviour, n);
            System.Array.Resize(ref _slotActive, n);
            System.Array.Resize(ref _spawnOwnerBySlot, n);
        }

        /// <summary>
        /// 보유 지급 + 빈 슬롯이 있으면 즉시 장착. 획득 경로(업그레이드 해금·탐험 드롭·던전 상자·상점)가
        /// 저마다 같은 장착 루프를 복사하지 않도록 여기 한 곳에 둔다.
        /// 이미 가진 유물이면 <see cref="RelicInventory.Grant"/>가 무시하므로 여러 번 불러도 안전하다.
        /// </summary>
        public void GrantAndAutoEquip(RelicID id)
        {
            if (id == RelicID.None) return;

            bool wasOwned = Inventory.IsOwned(id);
            Inventory.Grant(id);
            if (wasOwned) return;

            for (int slot = 0; slot < Inventory.SlotCount; slot++)
            {
                if (Inventory.GetEquipped(slot) != RelicID.None) continue;
                EquipSlot(slot, id);
                break;
            }
        }

        /// <summary>
        /// 보유 회수. 장착 중이면 먼저 <see cref="UnequipSlot"/>로 내려 behavior 클론과 스폰물을
        /// 정리한 뒤 인벤토리에서 뺀다. 순서를 바꾸면 회수한 유물의 드론·빔이 씬에 남는다.
        /// </summary>
        public bool RevokeAndUnequip(RelicID id)
        {
            if (id == RelicID.None) return false;
            if (!Inventory.IsOwned(id)) return false;

            for (int slot = 0; slot < Inventory.SlotCount; slot++)
                if (Inventory.GetEquipped(slot) == id) UnequipSlot(slot);

            return Inventory.Revoke(id);
        }

        // 지형 파괴 대상 해석 — Digger와 동일 우선순위(정적청크 씬 > 무한맵).
        private static ITerrainManager ResolveTerrainManager()
        {
            ITerrainManager t = FindFirstObjectByType<StaticChunkTerrainManager>();
            if (t == null) t = FindFirstObjectByType<InfinityMapManager>();
            return t;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (_playerStat != null) _playerStat.UnregisterProvider(_statProvider);

            if (_ctx != null && _ctx.mining != null)
                _ctx.mining.DigSwing -= NotifyDigSwing;

            if (_controller != null)
            {
                _controller.Landed -= NotifyLanded;
                // 우리가 설정한 델리게이트일 때만 해제 (델리게이트 값 비교)
                System.Func<bool> mine = TryConsumeAirJump;
                if (_controller.AirJumpQuery == mine)
                    _controller.AirJumpQuery = null;
                System.Func<bool> mineGround = AllowGroundJump;
                if (_controller.GroundJumpQuery == mineGround)
                    _controller.GroundJumpQuery = null;
            }
        }

        // 어떤 유물이든 지면 점프를 가로채면(false) 일반 점프 억제. (고물 스프링 차징 점프)
        public bool AllowGroundJump()
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (_slotBehaviour[i] != null && !_slotBehaviour[i].AllowGroundJump())
                    return false;
            return true;
        }

        // ── 장착/해제 ──
        public void EquipSlot(int slot, RelicID id)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            UnequipSlot(slot); // 기존 것 정리

            if (!Inventory.Equip(slot, id)) return;
            if (id == RelicID.None) return;

            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            if (so == null || so.behaviour == null)
            {
                Debug.LogWarning($"[RelicManager] RelicSO/behaviour 없음: {id}");
                return;
            }

            var behaviour = so.behaviour.Clone();
            int level = Mathf.Max(1, Inventory.GetLevel(id));
            behaviour.OnEquip(_ctx, level);

            _slotBehaviour[slot] = behaviour;
            _spawnOwnerBySlot[slot] = id;
            _slotActive[slot] = (so.type == RelicType.Active) ? new ActiveRelicState() : null;
        }

        public void UnequipSlot(int slot)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            var b = _slotBehaviour[slot];
            if (b != null)
            {
                b.OnUnequip();
                Despawn(_spawnOwnerBySlot[slot]);
            }
            _slotBehaviour[slot] = null;
            _slotActive[slot] = null;
            _spawnOwnerBySlot[slot] = RelicID.None;
            Inventory.Unequip(slot);
        }

        // ── 액티브 발동 (입력에서 호출) ──
        public void ActivateSlot(int slot)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            var b = _slotBehaviour[slot];
            var st = _slotActive[slot];
            if (b == null || st == null) return; // 패시브거나 빈 슬롯

            if (!b.CanActivate()) return; // 조건 불충족 → 발동 소모 안 함

            // 토글형: 지속 중 재입력이면 조기 종료
            if (b.IsToggle && st.CancelToCooldown())
            {
                b.OnActiveEnd();
                return;
            }

            if (st.TryActivate(b.GetDuration(), b.GetCooldown() * GetCooldownScaleFor(slot)))
            {
                // 공통 발동음(전자제품 가동). 전용 효과음이 있는 유물은 자기 것만 낸다.
                // 토글 조기종료는 위에서 return하므로 이 경로를 안 탄다.
                if (!b.HasOwnActivationSfx && SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SfxKeys.RelicActivate);

                b.OnActivate();
            }
        }

        // 발동 슬롯을 제외한 전 슬롯의 쿨타임 배율 곱(모래시계류). 발동 시점 값으로 확정.
        private float GetCooldownScaleFor(int activatingSlot)
        {
            float scale = 1f;
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (i != activatingSlot && _slotBehaviour[i] != null)
                    scale *= _slotBehaviour[i].GetCooldownScale();
            return scale;
        }

        // ── 매 프레임 훅 중계 ──
        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _slotBehaviour.Length; i++)
            {
                var b = _slotBehaviour[i];
                if (b == null) continue;

                b.OnUpdate();

                var st = _slotActive[i];
                if (st != null)
                {
                    var r = st.Tick(dt);
                    if (r.activeTick) b.OnActiveUpdate(r.activeElapsed);
                    if (r.activeEnded) b.OnActiveEnd();
                }
            }
        }

        // ── 코어 훅 중계 (통합 지점에서 호출) ──
        public void ApplyDigModifiers(ref DigParameters p)
        {
            // 채광 면허가 모자라 반경이 깎인 스윙은 유물이 밀어올리지 못한다.
            // 유물 훅은 전부 RadiusMultiplier에 '곱'하므로(도박꾼의 안경 highRoll,
            // mp3 Perfect 등) 그냥 두면 게이트 계수(0.05)가 배수만큼 커져
            // 면허 없이도 파이는 구멍이 된다. 게이트가 도구를 안 가리는 것과 같은 이유로
            // 유물도 안 가린다 — 못 파는 땅은 무엇을 끼든 못 파야 한다.
            //
            // ⚠ 훅 자체는 건너뛰지 않고 '반경만' 되돌린다. ModifyDigParameters에는
            //    반경 말고 부수효과가 들어 있다(mp3의 박자 판정 확정 JudgeThisFrame).
            //    통째로 return하면 면허가 모자란 층에서 박자 판정이 씹힌다.
            //    DamageMultiplier는 되돌리지 않는다 — 이 게이트는 지형 파기에 대한 것이고
            //    돌(DiggableRock) 데미지는 별개 규칙이다.
            float gatedRadius = p.RadiusMultiplier;
            bool blocked = p.MiningLevelBlocked;

            for (int i = 0; i < _slotBehaviour.Length; i++)
                _slotBehaviour[i]?.ModifyDigParameters(ref p);

            if (blocked) p.RadiusMultiplier = gatedRadius;
        }

        // 지형 파기 오버라이드 집계: 첫 true(첫 소유자 승리)에서 중단. Digger → PlayerMining → 여기.
        public bool TryOverrideTerrainDig(UnityEngine.Vector2 digPos, UnityEngine.Vector2 dir, float radius, int toolIndex)
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (_slotBehaviour[i] != null && _slotBehaviour[i].TryOverrideTerrainDig(digPos, dir, radius, toolIndex))
                    return true;
            return false;
        }

        public bool TryConsumeAirJump()
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (_slotBehaviour[i] != null && _slotBehaviour[i].TryConsumeAirJump())
                    return true;
            return false;
        }

        public void NotifyLanded()
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                _slotBehaviour[i]?.OnLanded();
        }

        // 파기 스윙 허브 relay: PlayerMining.DigSwing → 전 슬롯 OnDigSwing().
        public void NotifyDigSwing()
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                _slotBehaviour[i]?.OnDigSwing();
        }

        // ── 스폰물 수명 ──
        public GameObject Spawn(RelicID owner, GameObject prefab, Vector3 pos)
        {
            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (!_spawned.TryGetValue(owner, out var list))
            {
                list = new List<GameObject>();
                _spawned[owner] = list;
            }
            list.Add(go);
            return go;
        }

        public void Despawn(RelicID owner)
        {
            if (owner == RelicID.None) return;
            if (_spawned.TryGetValue(owner, out var list))
            {
                foreach (var go in list) if (go != null) Destroy(go);
                list.Clear();
                _spawned.Remove(owner);
            }
        }

        // ── UI 조회 ──
        public RelicPhase GetSlotPhase(int slot)
            => (_slotActive != null && slot >= 0 && slot < _slotActive.Length && _slotActive[slot] != null)
               ? _slotActive[slot].Phase : RelicPhase.Ready;

        public float GetSlotTimer(int slot)
            => (_slotActive != null && slot >= 0 && slot < _slotActive.Length && _slotActive[slot] != null)
               ? _slotActive[slot].Timer : 0f;

        // 강화 후 레벨 반영 (상점/디버그에서 호출)
        public void RefreshLevel(RelicID id)
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (_spawnOwnerBySlot[i] == id && _slotBehaviour[i] != null)
                    _slotBehaviour[i].OnLevelChanged(Inventory.GetLevel(id));
        }

        // ── 저장 연동 (CoinGameManager 패턴) ──
        public Data.RelicSaveData CaptureSaveData() => Inventory.ToSaveData();

        public void ApplySaveData(Data.RelicSaveData data)
        {
            if (data == null || !data.hasData) return;

            Inventory.LoadFrom(data);

            // 세이브의 칸 수 위에 업그레이드분을 얹는다. 아래 재장착 루프보다 먼저 해야
            // 늘어난 칸까지 한 번에 훑는다 — 슬롯 노드를 산 뒤에도 옛 세이브(slotCount 2)를
            // 불러오면 이 경로로 들어온다.
            SyncUpgradeSlotCount();

            // 슬롯 수 변경 반영
            int n = Inventory.SlotCount;
            if (_slotBehaviour == null || _slotBehaviour.Length != n)
            {
                _slotBehaviour = new RelicBehaviour[n];
                _slotActive = new ActiveRelicState[n];
                _spawnOwnerBySlot = new RelicID[n];
            }

            // 로드된 로드아웃대로 재장착 (behavior clone + OnEquip)
            for (int i = 0; i < n; i++)
            {
                var id = Inventory.GetEquipped(i);
                RebuildSlot(i, id);
            }

            // 로드가 Inventory를 덮어쓴 뒤라 여기서 다시 맞춘다 — 옛 세이브에는
            // 업그레이드로 지급되는 유물이 들어 있지 않다.
            SyncUpgradeUnlockedRelics();
        }

        // Inventory 상태는 그대로 두고 슬롯 behavior만 재구성 (중복 Equip 방지)
        private void RebuildSlot(int slot, RelicID id)
        {
            var prev = _slotBehaviour[slot];
            if (prev != null) { prev.OnUnequip(); Despawn(_spawnOwnerBySlot[slot]); }
            _slotBehaviour[slot] = null;
            _slotActive[slot] = null;
            _spawnOwnerBySlot[slot] = RelicID.None;

            if (id == RelicID.None) return;
            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            if (so == null || so.behaviour == null) return;

            var behaviour = so.behaviour.Clone();
            behaviour.OnEquip(_ctx, Mathf.Max(1, Inventory.GetLevel(id)));
            _slotBehaviour[slot] = behaviour;
            _spawnOwnerBySlot[slot] = id;
            _slotActive[slot] = (so.type == RelicType.Active) ? new ActiveRelicState() : null;
        }
    }
}
