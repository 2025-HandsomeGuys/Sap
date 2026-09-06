# 스프라이트 테두리 원샷 베이크 — 설계

작성일: 2026-07-16

## 목표

`SpriteRenderer` + `PolygonCollider2D`만 있는 **정적** 오브젝트의 스프라이트 알파를 모양으로 삼아,
지형과 동일한 **테두리 타일 + rim 아웃라인**을 구운 텍스처로 `SpriteRenderer.sprite`를 교체한다.

기존 지형 테두리 파이프라인(`ChunkData` + `TerrainVisualizer` + `ChunkJobScheduler` + `TerrainVisualJob`)을
최대한 재사용한다. 신규 렌더 로직은 작성하지 않는다.

## 배경 — 왜 재사용이 성립하는가

`TerrainVisualJob`의 픽셀 규칙(검증 완료, `TerrainJobs.cs`):

- 알파 0(공기) → 투명
- 내부(가장자리에서 먼 솔리드) → **`BasePixels` 원래 색 그대로 유지**
- 가장자리(`dist <= textureThicknessPx * 5`) → 그 위에 **테두리 타일(`BorderData`) 블렌드**
- 최외곽 → **rim 색(`RimColor`)**

따라서 스프라이트 RGBA를 그대로 `BasePixels`로 넣으면, 알파가 거리장을 결정하고
안쪽은 원래 그림 · 가장자리에는 지형과 동일한 테두리가 덧그려진다.

## 구성

| 요소 | 종류 | 역할 |
|------|------|------|
| `SpriteBorderBaker` | 순수 클래스 | 베이크 로직 캡슐화. `Sprite Bake(Sprite src, Texture2D borderTex, BakeSettings s)` |
| `SpriteTerrainBorder` | MonoBehaviour | 오브젝트에 부착, `Start`에서 1회 베이크 |
| `ChunkData.SetBorderData(...)` | 신규 헬퍼 | `SetSecondaryBorderData`와 대칭. Primary `BorderData` 세팅용 |

### `SpriteTerrainBorder` SerializeField

- `Texture2D borderTexture` — 지형과 동일 테두리 아틀라스
- `float textureThickness = 4f`
- `int pixelsPerUnit = 100`
- (rim은 `TerrainChunk` static 값을 그대로 사용 — 지형과 동일하게 표시)

## 베이크 흐름

기존 Job 파이프라인 재사용. 정적이므로 **메인스레드에서 원샷 동기 실행** 후 즉시 Dispose.

1. `src.texture`에서 스프라이트 rect 픽셀 `GetPixels32` → `w, h` 확보
2. `var data = new ChunkData(w, h)`
   - `data.LoadPixelData(pixels)` — 알파 = 점유
   - `data.LoadPixelInfo(null)` — BasePixels 알파에서 PixelInfo 자동 생성
3. `borderTex.GetPixels32()` → `data.SetBorderData(borderPixels, bw, bh)`
4. 출력 `var outTex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain:false)`
5. `var vis = new TerrainVisualizer(data, outTex, sr)`
   - `vis.TextureThickness = textureThickness; vis.PixelsPerUnit = pixelsPerUnit;`
6. `vis.UpdateVisualsFull(0, 0)` → `vis.ApplyTextureSync()`
   - 내부: `ScheduleInitLighting(0,0,W,H)`(= `SetDirtyRect` full + Downsample + Init)
     → Chamfer → Upsample → VisualJob(전 픽셀 dispatch) → 완료 + `Apply`
7. `outTex`로 새 `Sprite.Create(rect, pivot, pixelsPerUnit)` → `sr.sprite = baked`
8. `vis.Dispose(); data.Dispose();`

## PolygonCollider2D

테두리는 **솔리드 픽셀 안쪽으로만** 그려져 모양이 커지지 않는다.
→ 콜라이더 수정 불필요 (테두리는 시각 전용, 물리는 원래 폴리곤 유지).

## 제약 / 리스크

1. **스프라이트 텍스처 Read/Write Enabled 필수** — 아니면 `GetPixels32` 예외. import 설정 확인.
2. **투명 여백 필요** — BoundarySync가 없어(`onPreChamfer == null`), 아트가 텍스처 가장자리에 닿으면
   그 변은 테두리 없이 평평하게 잘린다(크래시 아님, 시각 문제). 스프라이트에 투명 여백을 둬 모양이
   텍스처 경계에 닿지 않게 한다.
3. **`ChunkData`는 Persistent NativeArray 다수 할당** → 반드시 `Dispose`. 씬에 오브젝트가 다수면
   스폰 프레임 스파이크 가능(정적이라 1회지만 필요 시 코루틴 분산).
4. 출력 텍스처 크기 = 스프라이트 픽셀 수. 큰 스프라이트면 메모리·잡 비용 증가.
5. `SecondaryBorder`, `IndestructibleMask` 미사용(후자는 zero-init라 자동으로 비활성).
6. rim은 `TerrainChunk` static 전역값 사용 — 오브젝트별 다른 rim이 필요해지면 그때 push/pop 또는
   `ScheduleVisualJob` rim 인자 오버로드가 필요(현 범위 밖).

## 검증된 사실 (코드 확인)

- `ChunkData(w,h)` 임의 크기 생성, `BorderData/BorderWidth/BorderHeight` public 필드 — `ChunkData.cs`
- `LoadPixelData(Color32[])`, `LoadPixelInfo(null)` 자동 생성 — `ChunkData.cs:143,157`
- `ScheduleInitLighting` → `SetDirtyRect(full)` → `_lastExt` full → VisualJob 전 픽셀 dispatch — `ChunkJobScheduler.cs:304,102`
- `UpdateVisualsFull` → `UpdateVisualsArea(..., onPreChamfer:null)` — `TerrainVisualizer.cs:71`
- VisualJob 픽셀 규칙 — `TerrainJobs.cs:102`
