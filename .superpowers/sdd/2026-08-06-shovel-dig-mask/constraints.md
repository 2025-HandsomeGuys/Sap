## Global Constraints

- **버전 관리는 UVCS다. `git add`/`git commit` 등 git 명령어를 쓰지 않는다.** 각 Task 끝의 "커밋"은 사람이 UVCS에서 직접 체크인한다. 계획에 git 명령을 넣지 않는다. (CLAUDE.md)
- **Unity Test Runner 실행은 사람이 직접 한다.** Claude는 테스트 파일을 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등 실행 도구를 호출하지 않는다. 테스트 통과를 다음 Task의 게이트로 요구하지 않고, 작성 후 그대로 진행한다. (CLAUDE.md)
- **더티 플래그는 `ChunkData` 메서드로만 세팅한다.** `data.MarkDirty()` / `data.MarkRenderDirty()`. 플래그 3개를 직접 나열하지 않는다. (CLAUDE.md §3) — 이 계획에서는 기존 `TerrainModifier.Dig`의 `data.MarkDirty()` 호출을 그대로 두므로 새로 추가할 일은 없다.
- **알파 임계값은 10.** `TerrainCarver.ALPHA_THRESHOLD`와 같은 값을 쓴다. 마스크 픽셀의 알파가 이 값 **초과**면 불투명(= 파임)으로 본다.
- **기본 동작 불변.** 마스크 미설정이 기본값이고, 그 상태에서 파기 결과가 지금과 픽셀 단위로 동일해야 한다.
- 신규 스크립트 파일 첫 줄에 `// @tags: ...` 주석을 단다 (프로젝트 검색 관례).

**설계 문서:** `docs/superpowers/specs/2026-08-06-shovel-dig-mask-design.md`
