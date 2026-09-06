// @tags: special-chunk, data-container, dto, json, trap, spawn, zone
using System.Collections.Generic;

/// <summary>
/// specialChunkSettings.json 의 데이터 모델.
/// JsonUtility.FromJson 으로 역직렬화된다.
/// JsonUtility 는 Dictionary 미지원 → spawnChances 는 List 로 저장 후 런타임에 Dict 변환.
/// </summary>
[System.Serializable]
public class SpecialChunkSettingsData
{
    public SpawningSection spawning = new SpawningSection();
    public TrapsSection    traps    = new TrapsSection();
    public EntitiesSection entities = new EntitiesSection();
    public DropsSection    drops    = new DropsSection();
    public ZonesSection    zones    = new ZonesSection();
    public PhysicsSection  physics  = new PhysicsSection();
    public SinkingPillarData sinkingPillar = new SinkingPillarData();

    // ─── Spawning ───────────────────────────────────────────────
    [System.Serializable]
    public class SpawningSection
    {
        public int minChunkSpacing      = 2;
        public int layerBoundarySpacing = 1;
        public List<SpawnChanceEntry> spawnChances = new List<SpawnChanceEntry>();

        private Dictionary<string, float> _chanceDict;

        /// <summary>List 를 Dictionary 로 변환하여 O(1) 조회를 제공한다. 최초 호출 시 1회 변환.</summary>
        public Dictionary<string, float> GetSpawnChanceDict()
        {
            if (_chanceDict != null) return _chanceDict;
            _chanceDict = new Dictionary<string, float>(spawnChances.Count);
            foreach (var entry in spawnChances)
                _chanceDict[entry.chunkTypeName] = entry.chance;
            return _chanceDict;
        }
    }

    [System.Serializable]
    public class SpawnChanceEntry
    {
        public string chunkTypeName;
        public float  chance;
    }

    // ─── Traps ──────────────────────────────────────────────────
    [System.Serializable]
    public class TrapsSection
    {
        public CollapseFloorData  collapseFloor  = new CollapseFloorData();
        public DelayedBlastData   delayedBlast   = new DelayedBlastData();
        public RollingRockData    rollingRock    = new RollingRockData();
        public StalactiteData     stalactite     = new StalactiteData();
        public ScrapExplosionData scrapExplosion = new ScrapExplosionData();
    }

    [System.Serializable]
    public class CollapseFloorData
    {
        public float collapseDelay    = 0.2f;
        public int   floorThicknessPx = 200;
    }

    [System.Serializable]
    public class DelayedBlastData
    {
        public float blastDelay    = 2.0f;
        public float blastRadius   = 0.3f;
        public float damageRadius  = 1.5f;
        public float staminaDamage = 20.0f;
    }

    [System.Serializable]
    public class RollingRockData
    {
        public float launchSpeed    = 8.0f;
        public float clearInterval  = 0.15f;
        public float clearRadius    = 0.4f;
        public float staminaDamage  = 30.0f;
        public float knockbackForce = 12.0f;
    }

    [System.Serializable]
    public class StalactiteData
    {
        public float detectionRange        = 3.0f;
        public float staminaDamage         = 20.0f;
        public float slowMultiplier        = 0.6f;
        public float slowDuration          = 2.0f;
        public float impactVibrationRadius = 2.0f;
    }

    [System.Serializable]
    public class ScrapExplosionData
    {
        public float explosionRadius = 3.0f;
        public float staminaDamage   = 30.0f;
        public float maxHp           = 30.0f;
        public int   scrapMin        = 3;
        public int   scrapMax        = 6;
        public float copperChance    = 0.20f;
        public float ironChance      = 0.15f;
    }

    // ─── Entities ───────────────────────────────────────────────
    [System.Serializable]
    public class EntitiesSection
    {
        public EntityHpData snowman      = new EntityHpData { maxHp = 5f  };
        public EntityHpData cable        = new EntityHpData { maxHp = 3f  };
        public EntityHpData trashWall    = new EntityHpData { maxHp = 30f };
        public EntityHpData crystalBlock = new EntityHpData { maxHp = 25f };
    }

    [System.Serializable]
    public class EntityHpData
    {
        public float maxHp;
    }

    // ─── Drops ──────────────────────────────────────────────────
    [System.Serializable]
    public class DropsSection
    {
        public TrashWallDropData trashWall = new TrashWallDropData();
    }

    [System.Serializable]
    public class TrashWallDropData
    {
        public int   scrapMin     = 3;
        public int   scrapMax     = 5;
        public float copperChance = 0.15f;
        public float ironChance   = 0.10f;
        public float scatterX     = 0.3f;
        public float scatterY     = 0.2f;
    }

    // ─── Zones ──────────────────────────────────────────────────
    [System.Serializable]
    public class ZonesSection
    {
        public OxidizedZoneData  oxidized  = new OxidizedZoneData();
        public BlackHoleZoneData blackHole = new BlackHoleZoneData();
    }

    [System.Serializable]
    public class OxidizedZoneData
    {
        public float penaltyPerTick = 5.0f;
        public float tickInterval   = 1.0f;
        public float minMaxStamina  = 20.0f;
    }

    [System.Serializable]
    public class BlackHoleZoneData
    {
        public float pullForce      = 8.0f;
        public float tickInterval   = 1.0f;
        public float damagePerTick  = 10.0f;
        public float knockbackForce = 12.0f;
    }

    // ─── Physics / Visuals ──────────────────────────────────────
    [System.Serializable]
    public class PhysicsSection
    {
        public float   icicleImpactRadius     = 2.0f;
        public float   vibrationDefaultRadius = 3.0f;
        public float   fallWarningDelay       = 0.5f;   // 종유석 낙하 예고 딜레이(초)
        public float[] damageStagedThresholds = new[] { 0.66f, 0.33f };
    }

    // ─── Sinking Pillar (용암 점프맵 가라앉는 기둥) ───────────────
    [System.Serializable]
    public class SinkingPillarData
    {
        public float sinkSpeed       = 0.5f;  // 가라앉는 속도 (유닛/초)
        public float riseSpeed       = 0.5f;  // 떠오르는 속도 (유닛/초)
        public float maxSinkDistance = 2.0f;  // 최대 하강 거리 (유닛)
        public float sinkDelay       = 0.5f;  // 밟은 뒤 내려가기 시작까지 대기 (초)
        public float riseDelay       = 1.0f;  // 떠난 뒤 올라오기 시작까지 대기 (초)
    }

    // ─── Mineral Rock (발광 광물돌) ──────────────────────────────
    public MineralRockSection mineralRock = new MineralRockSection();

    [System.Serializable]
    public class MineralRockSection
    {
        public float defaultMaxHp   = 8f;
        public int   defaultMinDrop = 2;
        public int   defaultMaxDrop = 4;
        public List<MineralRockEntry> overrides = new List<MineralRockEntry>();

        // spawnChances와 동일 패턴: List → Dictionary 1회 변환
        private Dictionary<string, MineralRockEntry> _dict;

        public ResolvedMineralRock Resolve(string mineralID)
        {
            if (_dict == null)
            {
                _dict = new Dictionary<string, MineralRockEntry>(overrides.Count);
                foreach (var e in overrides)
                    if (!string.IsNullOrEmpty(e.mineralID))
                        _dict[e.mineralID] = e;
            }

            if (!string.IsNullOrEmpty(mineralID) && _dict.TryGetValue(mineralID, out var entry))
                return new ResolvedMineralRock
                    { maxHp = entry.maxHp, minDrop = entry.minDrop, maxDrop = entry.maxDrop };

            return new ResolvedMineralRock
                { maxHp = defaultMaxHp, minDrop = defaultMinDrop, maxDrop = defaultMaxDrop };
        }
    }

    [System.Serializable]
    public class MineralRockEntry
    {
        public string mineralID;
        public float  maxHp;
        public int    minDrop;
        public int    maxDrop;
    }

    public struct ResolvedMineralRock
    {
        public float maxHp;
        public int   minDrop;
        public int   maxDrop;
    }

    // ─── Dokkaebi Cauldron (도깨비 가마솥) ───────────────────────
    public CauldronSection cauldron = new CauldronSection();

    [System.Serializable]
    public class CauldronSection
    {
        public float greatSuccessChance = 0.15f;
        public float successChance      = 0.45f;
        public float failChance         = 0.25f;
        public float greatFailChance    = 0.15f;
        public int   explosiveMin       = 3;
        public int   explosiveMax       = 6;
        public int   maxUses            = 3;

        public CauldronProbabilities ToProbabilities() => new CauldronProbabilities
        {
            greatSuccess = greatSuccessChance,
            success      = successChance,
            fail         = failChance,
            greatFail    = greatFailChance,
        };
    }
}
