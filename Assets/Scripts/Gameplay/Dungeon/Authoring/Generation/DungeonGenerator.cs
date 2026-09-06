// @tags: dungeon, generation, generator, orchestrator, seed
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    public class GenerationResult
    {
        public bool Success;
        public int Seed;
        public DungeonMapData Data;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
    }

    /// <summary>
    /// 프리셋 + 시드 → DungeonMapData. 파이프라인 전체가 System.Random 인스턴스 하나를 공유하므로
    /// 같은 시드는 항상 같은 맵을 만든다. 단계 순서를 바꾸면 같은 시드라도 결과가 달라진다.
    /// UnityEditor에 의존하지 않는다 — 나중에 런타임 생성으로 옮길 때 그대로 쓰기 위함.
    /// </summary>
    public static class DungeonGenerator
    {
        public static GenerationResult Generate(DungeonGenPresetSO preset, int seed)
        {
            var result = new GenerationResult { Seed = seed };

            if (preset == null)
            {
                result.Errors.Add("프리셋이 비어 있습니다.");
                return result;
            }

            var templates = LoadTemplates(preset, result);
            if (result.Errors.Count > 0) return result;
            if (templates.Count == 0)
            {
                result.Errors.Add("방 템플릿이 하나도 없습니다. 프리셋의 Room Template Files를 확인하세요.");
                return result;
            }

            var shapes = LoadShapes(preset, result);
            if (result.Errors.Count > 0) return result;
            if (shapes.Count == 0)
            {
                result.Errors.Add("형태 마스크가 하나도 없습니다. 프리셋의 Shape Files를 확인하세요.");
                return result;
            }

            var rng = new System.Random(seed);
            var mask = PickShape(shapes, rng);
            var plan = DungeonPathBuilder.Build(mask, preset.extraConnectionChance, rng);

            var composed = DungeonRoomComposer.Compose(plan, templates, preset, rng);

            // E/X 배치가 먼저 — 공동 메우기의 플러드필 출발점이 E다.
            DungeonEntryPlacer.Place(composed, plan, preset, rng);

            // 조립·배치 경고를 여기서 먼저 옮겨 담는다. 아래 연결성 판정이 실패로 빠져나가면
            // "'LUD' 템플릿이 없어…", "방 (r,c)에 바닥이 없어…" 같은 진짜 원인이 통째로 버려진다.
            result.Warnings.AddRange(composed.Warnings);
            composed.Warnings.Clear(); // 뒤에서 다시 담지 않도록 비운다

            int filled = DungeonCavityFiller.Fill(composed, preset, out bool exitReachable);
            if (!exitReachable)
            {
                result.Errors.Add("입구에서 출구까지 이어지지 않습니다. 방 템플릿의 개구부 위치 규약을 확인하세요.");
                return result;
            }
            if (filled > 0)
                result.Warnings.Add($"도달할 수 없는 빈칸 {filled}개를 암반으로 메웠습니다.");

            // 슬롯이 마지막 — 메워진 칸의 슬롯은 '벽에 묻힌 슬롯 삭제'가 알아서 지운다.
            DungeonRoomFiller.Fill(composed, preset, rng);

            // 슬롯 채우기가 새로 남긴 경고만 남아 있다(위에서 이미 비웠다).
            result.Warnings.AddRange(composed.Warnings);

            result.Data = new DungeonMapData
            {
                Name = $"gen_{seed}",
                CellSize = 1f,
                Width = composed.Width,
                Height = composed.Height,
                Tiles = composed.Tiles,
                Objects = composed.Objects,
                Links = composed.Links,
            };
            result.Success = true;
            return result;
        }

        private static List<DungeonRoomTemplate> LoadTemplates(DungeonGenPresetSO preset, GenerationResult result)
        {
            var all = new List<DungeonRoomTemplate>();
            if (preset.roomTemplateFiles == null) return all;

            foreach (var asset in preset.roomTemplateFiles)
            {
                if (asset == null) continue;

                var parsed = DungeonRoomTemplateParser.Parse(
                    asset.text, preset.roomWidth, preset.roomHeight, asset.name);

                result.Errors.AddRange(parsed.Errors);
                result.Warnings.AddRange(parsed.Warnings);
                if (parsed.Success) all.AddRange(parsed.Templates);
            }
            return all;
        }

        private static List<DungeonShapeMask> LoadShapes(DungeonGenPresetSO preset, GenerationResult result)
        {
            var all = new List<DungeonShapeMask>();
            if (preset.shapeFiles == null) return all;

            foreach (var asset in preset.shapeFiles)
            {
                if (asset == null) continue;

                var parsed = DungeonShapeParser.Parse(asset.text, asset.name);
                result.Errors.AddRange(parsed.Errors);
                result.Warnings.AddRange(parsed.Warnings);
                if (parsed.Success) all.AddRange(parsed.Masks);
            }
            return all;
        }

        private static DungeonShapeMask PickShape(List<DungeonShapeMask> shapes, System.Random rng)
        {
            int total = 0;
            foreach (var s in shapes) total += s.Weight > 0 ? s.Weight : 1;

            int roll = rng.Next(total);
            foreach (var s in shapes)
            {
                roll -= s.Weight > 0 ? s.Weight : 1;
                if (roll < 0) return s;
            }
            return shapes[shapes.Count - 1];
        }
    }
}
