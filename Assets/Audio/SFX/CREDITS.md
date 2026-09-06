# 효과음 크레딧 대장

`Assets/Audio/SFX/`에 넣는 모든 음원의 출처·라이선스 기록.
**음원을 받을 때마다 그 자리에서 한 줄 채운다.** 나중에 몰아서 하면 반드시 못 찾는다.

배포 전 이 파일을 근거로 인게임 크레딧 화면 / `CREDITS.txt`를 만든다.

---

## 채우는 법

| 열 | 내용 |
|---|---|
| **키** | `SfxKeys`의 값. 파일명도 **반드시 이것과 같아야** `Tools/Sound/Rescan SFX Folder`가 등록한다 (`player_jump.wav`) |
| **후보** | mewpot ID(= 다운로드한 곳). 여러 개면 그중 **하나만 골라** 쓰고 나머지는 지운다 (한 사운드 = 한 클립) |
| **원저작자** | **원본 사이트에 표기된 이름.** mewpot이 아니다 (아래 ⚠ 참조) |
| **원본 출처** | 원본 페이지 URL (SoundBible 등). mewpot URL이 아니다 |
| **라이선스** | `CC0` / `Public Domain` / `CC BY 3.0` 등. **`CC BY-NC`는 상업 이용 금지라 쓰면 안 된다** |
| **수정** | 자르기·볼륨·피치·포맷 변환을 했으면 `O`. CC BY는 수정 사실 명시가 의무다 |

mewpot URL 형식: `https://www.mewpot.com/sound-effects/{ID}`

### ⚠ mewpot은 배포처(distributor)지 저작권자가 아니다

mewpot에서 받으면 이런 문구가 딸려 온다:

```
Thanks to, Mike Koenig
From: http://soundbible.com/1599-Store-Door-Chime.html
Distributor: ... https://www.mewpot.com
```

여기서 **저작자는 Mike Koenig, 원본은 SoundBible**이다. 크레딧에 적어야 할 대상은
그쪽이고, mewpot 표기는 **라이선스 의무가 아니다**(저쪽의 홍보 요청). 넣어도 무방.

그리고 저 문구만으로는 **CC BY 요건이 안 채워진다 — 라이선스 이름과 링크가 없다.**
직접 추가해야 한다.

**라이선스는 반드시 원본 페이지에서 확인한다.** SoundBible만 해도 음원마다
`Attribution 3.0`(=CC BY 3.0) / `Public Domain` / `Sampling Plus`가 섞여 있고,
재배포처는 이걸 뭉뚱그리거나 틀리게 적는 경우가 있다.
**Public Domain이면 표기 의무 자체가 없다.**

### CC BY 표기에 필요한 4가지

1. 저작자 이름 (지정 표기명 그대로)
2. 저작물 제목 (제공된 경우)
3. 원본 링크 + 라이선스 링크 (`https://creativecommons.org/licenses/by/3.0/`)
4. 수정했다면 수정 사실

표기 예시 (위 Mike Koenig 사례):

```
"Store Door Chime" by Mike Koenig — http://soundbible.com/1599-Store-Door-Chime.html
Licensed under CC BY 3.0 (https://creativecommons.org/licenses/by/3.0/)
Modified: 길이 편집, 볼륨 조정
```

### 라이선스 판별

| 표기 | 상업 게임 사용 | 출처 표기 |
|---|---|---|
| `CC0` / Public Domain | ✅ | 불필요 (해도 무방) |
| `CC BY` | ✅ | **필수** |
| `CC BY-SA` | ⚠ ShareAlike — 상업 프로젝트에선 피할 것 | 필수 |
| `CC BY-NC` | ❌ **금지** | — |

⚠ 같은 사이트라도 파일마다 라이선스가 다르다. **받을 때마다 배지를 확인한다.**
다운로드 시점의 라이선스 표기를 스크린샷이나 URL+날짜로 남겨두면 나중에 분쟁 시 근거가 된다.
CC 라이선스는 취소 불가라, 정당하게 받은 것은 원저작자가 내려도 계속 쓸 수 있다.

---

## 플레이어

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `jump_grass` | 지상 점프 시작 | Footsteps-Essentials `Grass_Jump_Land_01` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `land_grass` | 지상 착지 | Footsteps-Essentials `Grass_Jump_Land_05` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `jump_dirt` | 지하 점프 시작 | Footsteps-Essentials `DirtyGround_Jump_Start_01` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `land_dirt` | 지하 착지 | Footsteps-Essentials `DirtyGround_Jump_Start_03` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `player_heartbeat` | 심장 (스태미나 낮음, 루프) | 12127 / 438 | | | | |
| `player_hurt` | 위험물 피격 | Kenney impact-sounds `impactWood_medium_000` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `step_grass` | 지상 걷기 — 왼발 | Footsteps-Essentials `Walk_Grass_Mono_04` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `step_grass_b` | 지상 걷기 — 오른발 | Footsteps-Essentials `Walk_Grass_Mono_05` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `step_dirt` | 지하 걷기 — 왼발 | Footsteps-Essentials `DirtyGround_Walk_03` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `step_dirt_b` | 지하 걷기 — 오른발 | Footsteps-Essentials `DirtyGround_Walk_04` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `step_hardstone` | 지하 2층 걷기 — 왼발 | Footsteps-Essentials `Snow_Walk_08` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |
| `step_hardstone_b` | 지하 2층 걷기 — 오른발 | Footsteps-Essentials `Snow_Walk_09` | ⚠ 미확인 | ⚠ 미확인 | ⚠ **미확인** | 이름만 변경 |

## 채굴

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `dig_swing` | 곡괭이 휘두르기 | 2559 / 2592 / 4600 / 2579 | | | | |
| `dig_sap` | 삽 파기 | Western Demo Audio Assets `Player/Footstep Wood Jump` | (팩 저작자 — 확인 필요) | Unity Asset Store — Western Demo Audio Assets | 에셋스토어 팩 | 이름만 변경 |
| `dig_hit` | 곡괭이 부딪힘 (명중) | Kenney sci-fi-sounds `impactMetal_002` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `dig_blocked` | 못 캐는 것 두드림, 튕김 | 1028 | | | | |
| `rock_break` | 돌 부서짐 | 284 | | | | |
| `ice_break` | 전구·얼음층 바위 부서짐 | 1525 | | | | |
| `explosion` | 폭발 공통 | 1523 / 1062 / 374 | | | | |

## 도구 · 유물

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `drill_on` | 드릴 켜질 때 버튼 | 466 | | | | |
| `drill_motor` | 드릴 모터 (루프) | SoundBits Free Sound FX `emt2_milling-machine_02_01` | SoundBits | Unity Asset Store — SoundBits \| Free Sound FX Collection (id 31837) | 에셋스토어 무료 팩 | 이름만 변경 |
| `power_down` | 배터리 소진, 전원 꺼짐 | 526 | | | | |
| `jetpack_thrust` | 제트팩 분사 (루프) | Kenney sci-fi-sounds `spaceEngineLow_000` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `relic_lightning` | 천둥 (번개 유물) | 268 / 262 | | | | |
| `relic_detect_ping` | 탐지기 | 417 | | | | |
| `relic_portal_return` | 귀환석 (일회용 포탈 귀환) | 4440 | | | | |
| `relic_invincible` | 무적 (포스필드) | Kenney sci-fi-sounds `forceField_004` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `relic_gravity` | 반중력 지속 (**루프**) | Kenney sci-fi-sounds `spaceEngine_000` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `relic_activate` | 전자제품 가동음 (유물 공통) | 502 | | | | |

## 이동 · 전환

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `elevator_move` | 엘리베이터 묵직 | 536 | | | | |
| `portal_enter` | 훅, 구멍 들어갈 때 | 2574 | | | | |

## 경제 · UI

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `ui_button` | 귀여운 버튼 클릭 | Kenney (아래 참조) | Kenney | kenney.nl/assets/interface-sounds | CC0 | 이름만 변경 |
| `ui_terminal_on` | 컴퓨터 켜지는 소리 | 1074 / 1475 | | | | |
| `mineral_pickup` | 광물 획득 (줍기) | Western Demo Audio Assets `Objects/Glass Pickup` | (팩 저작자 — 확인 필요) | Unity Asset Store — Western Demo Audio Assets | 에셋스토어 팩 | 이름만 변경 |
| `shop_sell` | 광물 판매 (+ 정산창 동전음 재사용) | 887 | | | | |
| `shop_buy` | 상점 구매 | 2246 / 4301 / 509 | | | | |
| `upgrade_unlock` | 업그레이드 트리 해금 (단발) | Casual Game Sounds U6 `DM-CGS-26` | (팩 저작자 — 확인 필요) | Unity Asset Store — Casual Game Sounds U6 | 에셋스토어 팩 (동봉 `license.pdf`) | 이름만 변경 |
| `upgrade_tier` | 상위 계층(Tier) 해금 | Kenney interface-sounds `confirmation_002` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `equip_enhance` | 장비 강화 +N (금속 **3연타**) | Kenney impact-sounds `impactMetal_light_003` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `cauldron_brew` | 도깨비 가마솥 | 1533 | | | | |
| `coin_tick` | 드럼롤, 코인 결과 발표 | 1207 | | | | |

## 하루 사이클 · 결과

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `sleep_snore` | 코고는 소리 | 503 / 460 | | | | |
| `daysummary_profit` | 정산 흑자 (Steel 징글) | Kenney music-jingles `jingles_STEEL10` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `daysummary_loss` | 정산 적자 (Steel 징글) | Kenney music-jingles `jingles_STEEL07` | Kenney | https://kenney.nl/assets | CC0 | 이름만 변경 |
| `quest_complete` | 자전거 벨 (서브퀘스트 완료) | 2608 | | | | |
| `mission_success` | 미션 성공 (메인퀘스트·던전 클리어) | 2657 | | | | |
| `game_over` | 실패 (죽음·긴급탈출) | 4591 | | | | |

## 앰비언스 (루프)

루프 음원은 **시작·끝이 자연스럽게 이어지는지** 확인할 것. 이음매가 튀면 몇 초마다 딸깍거린다.

| 키 | 용도 | 후보 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|---|
| `amb_surface_day_cicada` | 매미 (지상 낮, Primary) | 1052 | | | | |
| `amb_surface_day_birds` | 숲 새 지저귐 (지상 낮, Secondary) | 480 | | | | |
| `amb_surface_night_insects` | 밤 풀벌레 (지상 밤) | 1024 / 817 | | | | |
| `amb_layer_wind` | 바람 (지하 2층 HardStone) | 4270 / 4271 | | | | |
| `amb_cave_drip` | 동굴 물방울 (지하 3층 이하) | 1402 | | | | |

---

## 기타 (SFX 폴더 밖)

`Tools/Sound/Rescan SFX Folder`의 스캔 대상이 아니라 별도로 관리되는 음원.
기존에 프로젝트에 들어와 있던 것들이라 출처가 불명확할 수 있다. **배포 전 반드시 확인.**

| 위치 | 내용 | 원저작자 | 원본 출처 | 라이선스 | 수정 |
|---|---|---|---|---|---|
| `Assets/Audio/BGM/UpGround/MorningBGM.mp3` | 지상 낮 BGM | | | | |
| `Assets/Audio/BGM/UpGround/Night.wav` | 지상 밤 BGM | | | | |
| `Assets/Sound/Coin/*.wav` | 코인 미니게임 7종 | | | | |
| `Assets/Audio/Click_SFX_Pack/` | 클릭음 팩 194개 (현재 미사용) | | | | |
| `Assets/Audio/SoundPacks/kenney_interface-sounds/` | Kenney Interface Sounds 100개 (원본 보관소) | Kenney | https://kenney.nl/assets/interface-sounds | **CC0** | — |
| `Assets/Audio/SoundPacks/kenney_impact-sounds.zip` | Kenney Impact Sounds (미압축) | Kenney | https://kenney.nl/assets | **CC0** | — |

**Kenney 에셋은 CC0**라 크레딧 표기 의무가 없다. 다만 관례상 적어주는 게 좋고,
어디서 왔는지 기록은 남겨야 나중에 헷갈리지 않는다.
원본 `License.txt`가 팩 폴더 안에 그대로 있으니 근거로 보관한다.

### ⚠ `SoundPacks/`는 버전 관리에서 제외돼 있다 (로컬 전용)

`ignore.conf`에 `**/Assets/Audio/SoundPacks`가 등록돼 있다. 약 15MB / 1300여 파일이라
받아온 원본 팩을 통째로 올리지 않는다.

**게임 동작에는 영향이 없다.** 실제로 쓰는 음원은 `SFX/`에 키 이름으로 **복사**돼 있고
`SoundData.asset`도 그 복사본을 참조한다. 팩 원본을 참조하는 에셋은 하나도 없다.

다른 작업자가 팩을 열어보고 싶으면 [kenney.nl](https://kenney.nl/assets)에서 다시 받으면
된다(전부 CC0, 무료). 받아서 `Assets/Audio/SoundPacks/`에 풀면 아래 작업 흐름 그대로 쓸 수 있다.

### ⚠ 음원 팩은 `Assets/Audio/SoundPacks/`에 둔다

`Assets/Audio/SFX/`는 **실제로 쓰는 것만**(현재 44개) 두는 곳이다. 팩을 통째로 여기 풀면
임포터가 수백 개를 쓰레기 키로 등록할 수 있다(임포터가 최상위만 스캔하도록 막아뒀지만,
애초에 섞어두지 않는 게 맞다).

**작업 흐름**: 팩은 `SoundPacks/`에 풀어두고 → 쓸 파일만 `SFX/` 최상위에
**키 이름으로 복사** → `Tools/Sound/Rescan SFX Folder`.

---

## 받은 원문 보관

다운로드 사이트가 준 크레딧 문구를 **일단 여기 그대로 붙여넣는다.**
나중에 위 표로 옮기고 나면 지운다. 원문을 보관하는 이유는 표에 요약하면서
저작자 표기명을 임의로 바꾸는 실수를 막기 위해서다 (CC BY는 "지정된 표기명 그대로"를 요구한다).

```
Thanks to, Mike Koenig
From: http://soundbible.com/1599-Store-Door-Chime.html

Distributor: Stop worrying about music & free sound effects with Mewpot'
https://www.mewpot.com
```
↑ 어느 키에 쓸 음원인지 아직 미정. 정해지면 위 표로 옮길 것.
**라이선스가 안 적혀 있으므로 SoundBible 원본 페이지에서 확인 필요.**

---

## 참고

- 키 정의: `Assets/Scripts/_Core/Managers/SfxKeys.cs`
- 훅 지점 전체 매핑: `Assets/Docs/audio/sound-system-design.md` §4
- 등록: 파일명을 키와 같게 해서 이 폴더에 넣고 `Tools/Sound/Rescan SFX Folder` 실행.
  아직 안 채운 키가 콘솔에 나열된다
