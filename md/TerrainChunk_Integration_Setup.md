# TerrainChunk 통합 설정 가이드

## 개요
이 문서는 `TerrainChunk.cs`를 `WorldGenerator.cs`의 청크 시스템에 통합하기 위한 Unity Inspector 설정 가이드입니다.

## 계산 배경
- **TerrainChunk**: width=1000, height=1000, PPU=100f → 월드 크기 = 10 유니티 단위
- **WorldGenerator**: chunkSize=32, cellSize=0.3125f → 청크 월드 크기 = 32 × 0.3125 = 10 유니티 단위
- **결과**: 1:1 매핑 완료

## Unity Inspector에서 수동 설정 필요 (khbScene 1)

### 1. WorldGenerator 오브젝트 설정

1. **Hierarchy에서 WorldGenerator 오브젝트 선택**
2. **Inspector에서 WorldGenerator 컴포넌트 찾기**
3. **`Cell Size` 필드를 `0.3125`로 변경**
   - 기본값: 0.05
   - 변경값: **0.3125**

### 2. Grid 오브젝트 설정 (Tilemap의 부모)

1. **Hierarchy에서 Grid 오브젝트 찾기** (Tilemap의 부모 오브젝트)
2. **Inspector에서 Grid 컴포넌트 찾기**
3. **`Cell Size`를 `(0.3125, 0.3125, 1)`로 변경**
   - 기본값: (0.05, 0.05, 0)
   - 변경값: **(0.3125, 0.3125, 1)**

### 3. Region Prefab 확인

1. **Project 창에서 `Assets/Prefabs/World/Region.prefab` 열기**
2. **Prefab 내부의 Grid 오브젝트 선택**
3. **Inspector에서 Grid 컴포넌트의 `Cell Size`를 `(0.3125, 0.3125, 1)`로 변경**
4. **Prefab 저장** (Ctrl+S 또는 상단 메뉴: Prefab > Save)

### 4. 확인 사항

- **WorldManager의 `groundTilemap` 참조**가 올바른지 확인
- **WorldManager의 `worldGenerator` 참조**가 올바른지 확인
- **WorldManager의 `playerTransform` 참조**가 올바른지 확인

## 코드 변경 사항

### WorldGenerator.cs
- `cellSize` 기본값이 `0.3125f`로 변경됨

### TerrainChunk.cs
- `Initialize(ChunkData, Vector2Int, float)` 메서드 추가
- PPU는 100f로 고정 (계산하지 않음)
- `GenerateTextureFromChunkData()` 메서드 추가
- 좌표 변환 유틸리티 메서드 추가

### WorldManager.cs
- `PlaceTilesForChunk()`에서 TerrainChunk를 동적으로 생성하고 `Initialize()` 호출
- `UnloadChunk()`에서 TerrainChunk 제거
- `_activeTerrainChunks` 딕셔너리로 TerrainChunk 관리

## 주의사항

1. **씬 파일은 Unity Editor에서 직접 수정해야 합니다**
   - YAML 직접 수정은 위험합니다
   - Inspector를 통한 설정이 안전합니다

2. **기존에 배치된 타일들이 있다면 위치가 변경될 수 있습니다**
   - cellSize 변경으로 인해 타일 위치가 재조정됩니다

3. **다른 씬들도 동일하게 설정해야 일관성 유지**
   - 모든 씬의 Grid와 WorldGenerator cellSize를 동일하게 설정해야 합니다

4. **Region Prefab 수정 후 모든 씬에 반영**
   - Prefab을 수정하면 해당 Prefab을 사용하는 모든 씬에 자동 반영됩니다

## 테스트 체크리스트

- [ ] WorldGenerator의 cellSize가 0.3125로 설정됨
- [ ] Grid의 Cell Size가 (0.3125, 0.3125, 1)로 설정됨
- [ ] Region Prefab의 Grid Cell Size가 (0.3125, 0.3125, 1)로 설정됨
- [ ] 게임 실행 시 TerrainChunk가 올바르게 생성됨
- [ ] 청크들이 겹치지 않고 올바른 위치에 배치됨
- [ ] 땅 파기 기능이 정상 작동함
- [ ] 테두리 렌더링이 정상 작동함

