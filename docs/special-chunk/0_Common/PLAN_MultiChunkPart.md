# Plan: IMultiChunkPart — 멀티청크 상대 위치 주입
@tags: special-chunk, multi-chunk, IMultiChunkPart, coordinate, plan, sub-chunk

## 목표
긴 스페셜청크(멀티청크)를 구성하는 각 서브청크 컴포넌트가
자신의 앵커 기준 상대 오프셋(relativeOffset)을 런타임에 알 수 있도록
인터페이스 + 주입 메커니즘을 추가한다.

## 변경 파일
| 파일 | 변경 |
|------|------|
| `SpecialChunks/Interfaces/IMultiChunkPart.cs` | **신규** — 상대 위치 수신 인터페이스 |
| `SpecialChunkManager.cs` | `SpawnSpecialChunkIfPossible` 에서 IMultiChunkPart 주입 |

## 구현 단계

### 1. IMultiChunkPart 인터페이스
```csharp
public interface IMultiChunkPart
{
    /// <param name="anchorCoord">앵커 청크의 청크 좌표</param>
    /// <param name="relativeOffset">앵커 기준 나의 오프셋 (청크 단위). 앵커 본인은 (0,0)</param>
    void OnMultiChunkSpawned(Vector2Int anchorCoord, Vector2Int relativeOffset);
}
```

### 2. SpawnSpecialChunkIfPossible 수정
앵커 스폰 후:
```
anchorChunk.GetComponents<IMultiChunkPart>() 호출
→ OnMultiChunkSpawned(coord, Vector2Int.zero)
```
서브청크 스폰 후:
```
subChunk.GetComponents<IMultiChunkPart>() 호출
→ OnMultiChunkSpawned(coord, subDef.offset)
```

## 주의사항
- SOLID: IMultiChunkPart는 2메서드 미만으로 ISP 준수
- OCP: SpecialChunkManager는 IMultiChunkPart 구현체를 모른다 (DIP)
- 기존 단일 청크는 영향 없음 (subChunks == null/empty 이면 앵커도 주입 안 함)
- 테스트: TrashWallGenerator 등 기존 컴포넌트에 IMultiChunkPart 추가해 offset 로그 확인
