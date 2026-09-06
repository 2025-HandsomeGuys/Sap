// @tags: relic, drop, roll, rock, chest, dungeon, pity, exploration, layer
using Relic.Data;
using UnityEngine;

namespace Relic.Drop
{
    /// <summary>
    /// 유물 탐험 드롭의 런타임 진입점. 돌 완파(<see cref="DiggableRock"/>)와 던전 상자
    /// (<see cref="DungeonRewardPickup"/>)가 여기로만 들어온다.
    ///
    /// 하는 일은 넷이다 — 지층 해석 → 확률·피티 판정 → <see cref="RelicDropTable"/>로 추첨 →
    /// <see cref="WorldRelicPickup"/> 생성. 추첨 규칙 자체는 순수 로직으로 분리해 뒀다(테스트 대상).
    ///
    /// 유물은 <b>바닥에 떨어뜨리고 F키로 줍게</b> 한다. 즉시 인벤토리에 꽂으면 "돌을 깼더니 갑자기
    /// 유물이 생겼다"가 되어 어디서 나왔는지 안 보이고, 광물과 획득 방식이 달라 손에 안 익는다.
    ///
    /// 설계 배경: <c>Assets/Docs/relic-exploration-drop.md</c>
    /// </summary>
    public static class RelicDropRoller
    {
        // 드롭 판정용 난수. UnityEngine.Random은 지형 생성 시드와 같은 스트림이라
        // 여기서 뽑으면 청크 생성 결과가 유물 운에 따라 흔들릴 수 있다. 별도 스트림으로 격리한다.
        private static readonly System.Random s_rng = new System.Random();

        /// <summary>드롭 로그 스위치. 밸런싱 중에만 켠다.</summary>
        public static bool VerboseLog = false;

        // ================================================================
        //  돌 완파
        // ================================================================

        /// <summary>
        /// 돌 하나를 완파했을 때의 유물 판정.
        /// </summary>
        /// <param name="chunkY">돌이 있던 청크의 Y(지상 0, 아래로 음수). 지층 해석에 쓴다</param>
        /// <param name="isMineralRock">광물돌이면 확률 배율을 받는다</param>
        /// <param name="worldPos">드롭 위치(돌 중심)</param>
        public static void TryDropFromRock(int chunkY, bool isMineralRock, Vector3 worldPos)
        {
            var s = RelicDropSettingsLoader.Settings;
            if (s == null || !s.enabled) return;

            // Instance가 잡혀 있으면 즉시 반환된다. 없을 때만 플레이어에 부착하는 안전망이 돈다.
            var mgr = RelicManager.EnsureInScene();
            if (mgr == null) return;   // 플레이어가 없는 씬(타이틀 등) — 조용히 통과

            float chance = s.rock.baseChance * (isMineralRock ? s.rock.mineralRockMultiplier : 1f);
            bool pityHit  = s.rock.pityRocks > 0 && mgr.Inventory.RockDropPity >= s.rock.pityRocks;

            if (!pityHit && s_rng.NextDouble() >= chance)
            {
                mgr.Inventory.RockDropPity++;
                return;
            }

            string tileType = ResolveTileType(chunkY);
            if (!RelicDropTable.TryPick(s, tileType, mgr.Inventory.IsOwned, s_rng, out var picked))
            {
                // 이 지층에서 줄 게 없다(전부 보유 등). 피티는 소모하지 않고 그대로 둔다 —
                // 새 티어에 진입한 순간 바로 나오게 하기 위해서다.
                if (VerboseLog) Debug.Log($"[RelicDrop] {tileType}: 후보 없음 — 피티 유지({mgr.Inventory.RockDropPity})");
                return;
            }

            mgr.Inventory.RockDropPity = 0;
            Spawn(picked, worldPos, s.rock.sortingOrder,
                  $"돌 완파 ({tileType}{(pityHit ? ", 피티" : "")})");
        }

        // ================================================================
        //  던전 상자
        // ================================================================

        /// <summary>
        /// 던전 상자를 열었을 때의 유물 판정. 지층은 <b>던전 문이 있던 청크 깊이</b>로 본다 —
        /// 던전 자체는 지형이 생성되지 않는 먼 좌표에 있어 발밑에서 지층을 읽을 수 없다.
        /// </summary>
        /// <returns>유물을 떨어뜨렸으면 true</returns>
        public static bool TryDropFromChest(Vector3 worldPos)
        {
            var s = RelicDropSettingsLoader.Settings;
            if (s == null || !s.enabled) return false;

            var mgr = RelicManager.EnsureInScene();
            if (mgr == null) return false;

            bool pityHit = s.dungeonChest.pityChests > 0 &&
                           mgr.Inventory.ChestDropPity >= s.dungeonChest.pityChests;

            if (!pityHit && s_rng.NextDouble() >= s.dungeonChest.chance)
            {
                mgr.Inventory.ChestDropPity++;
                return false;
            }

            string tileType = ResolveTileType(0);   // 던전 안이므로 문 좌표로 대체된다
            if (!RelicDropTable.TryPick(s, tileType, mgr.Inventory.IsOwned, s_rng, out var picked))
            {
                if (VerboseLog) Debug.Log($"[RelicDrop] 상자 {tileType}: 후보 없음 — 피티 유지({mgr.Inventory.ChestDropPity})");
                return false;
            }

            mgr.Inventory.ChestDropPity = 0;
            return Spawn(picked, worldPos, s.rock.sortingOrder,
                         $"던전 상자 ({tileType}{(pityHit ? ", 피티" : "")})") != null;
        }

        // ================================================================
        //  디버그 진입점 (F9 콘솔)
        // ================================================================

        /// <summary>
        /// 이 좌표가 속한 지층의 tileType. 던전 안이면 던전 문 깊이를 본다.
        /// 콘솔이 "지금 여기서 뭐가 나오는가"를 보여줄 때 쓴다.
        /// </summary>
        public static string CurrentTileType(Vector3 worldPos)
            => ResolveTileType(ChunkCoords.ToChunk(worldPos).y);

        /// <summary>
        /// 확률·피티를 건너뛰고 <b>지금 지층의 드롭표로</b> 1종 뽑아 떨어뜨린다.
        /// 실제 드롭과 같은 경로(지층 해석 → 추첨 → 픽업 생성)를 타므로 표 자체를 검증할 수 있다.
        /// 피티 카운터는 건드리지 않는다 — 치트가 밸런스 상태를 오염시키지 않게.
        /// </summary>
        public static bool ForceDrop(Vector3 worldPos, out RelicID picked)
        {
            picked = RelicID.None;

            var s = RelicDropSettingsLoader.Settings;
            if (s == null) return false;

            var mgr = RelicManager.EnsureInScene();
            System.Func<RelicID, bool> owned = null;
            if (mgr != null) owned = mgr.Inventory.IsOwned;   // 매니저 없는 씬이면 전부 미보유로 본다

            string tileType = ResolveTileType(ChunkCoords.ToChunk(worldPos).y);
            if (!RelicDropTable.TryPick(s, tileType, owned, s_rng, out picked)) return false;

            return Spawn(picked, worldPos, s.rock.sortingOrder, $"콘솔 강제 드롭 ({tileType})") != null;
        }

        // ================================================================
        //  내부
        // ================================================================

        /// <summary>
        /// 청크 Y → 지층 tileType 문자열. TileDataManager가 없는 씬이면 최상층(Dirt)으로 폴백한다.
        /// <c>DiggableRock.DropRareMinerals</c>와 같은 해석(<c>GetTileTypeAtDepth</c>에 부호 있는 Y)이다.
        ///
        /// 던전 안에서는 넘어온 chunkY를 <b>버리고 던전 문이 있던 깊이</b>를 쓴다.
        /// 던전은 지형이 생성되지 않는 먼 좌표에 통째로 놓이고 그 안의 돌은 청크에 속하지 않아
        /// chunkY가 0으로 잡힌다 — 그대로 두면 최하층 던전에서 1티어 유물이 나온다.
        /// </summary>
        private static string ResolveTileType(int chunkY)
        {
            if (DungeonOverlayController.IsInDungeon)
                chunkY = DungeonStateStore.CurrentInstance.y;

            if (TileDataManager.Instance == null) return TileType.Dirt.ToString();
            return TileDataManager.Instance.GetTileTypeAtDepth(-Mathf.Abs(chunkY)).ToString();
        }

        private static WorldRelicPickup Spawn(RelicID id, Vector3 worldPos, int sortingOrder, string reason)
        {
            var pickup = WorldRelicPickup.Create(id, new Vector3(worldPos.x, worldPos.y, -1f), sortingOrder);
            if (pickup == null)
            {
                // 여기까지 왔는데 실패했으면 데이터 문제(RelicDatabase 누락)다. 피티는 이미 초기화됐고
                // 유물은 안 나왔으므로 조용히 넘기면 "가끔 아무것도 안 나온다"로만 보인다.
                Debug.LogError($"[RelicDrop] {id} 픽업 생성 실패 — {reason}. RelicDatabase 등록을 확인할 것");
                return null;
            }

            Debug.Log($"[RelicDrop] 유물 등장: {id} — {reason}");
            return pickup;
        }
    }
}
