# WeldingBot — 조선소 용접 로봇 시뮬레이터 (처음부터 만드는 순서)

갠트리 아래에 거꾸로 매단 6축 용접 로봇이 판재 이음부를 따라 용접하는 시뮬레이터입니다.
Unity CLI + Pipeline 패키지로 Editor를 조작해 만들었습니다 (CleanerBot과 같은 방식).

- 환경: Windows, Unity 6000.6.3f1 (URP, `com.unity.template.urp-blank`), Unity CLI 1.0.0-beta.11
- 경로: `D:\UnityAIProjs\DigitalTwin\WeldingBot`
- 패키지 추가: `com.unity.pipeline` 0.8.0-exp.1 (`[CliCommand]` → `unity command wb_*`)
- 작업 순서: C# 작성 → `AgentTools\dryrun.ps1` (컴파일 확인) → `Refresh.cs` → `wb_*` 명령 → `snap.ps1` 스크린샷 → git 커밋

## 1. 구조

| 파일 | 내용 |
|---|---|
| `RobotKinematics.cs` | 6축 로봇(구면 손목) 정기구학·해석적 역기구학(최대 8해), 자세 비용, 관절 한계 |
| `Plates.cs` | 판재(`PlateSpec`), 작업(`WeldJob`), 배치 도우미(`JobBuilder`: Flat / WebOn / Hinged / Panel / Support), 프리셋 5종 |
| `Seams.cs` | 용접선 자동 추출: 필릿(판 모서리가 다른 판 면에 닿음, 양쪽) + 맞대기(모서리끼리, 각도 포함), 다른 부재가 지나가는 구간 분할, 자세 분류(1F/2F/3F/4F) |
| `WeldPlanner.cs` | 갠트리 위치 탐색(고정 스테이션 → 실패 시 갠트리 추적 용접), 토치 각도 프로파일, 링크 충돌 검사(캡슐 vs 판재) |
| `WeldSession.cs` | 판재 → 용접선 → 계획 → 타임라인, 순서 결정(현재 스테이션 재사용 우선), 공중 이동 충돌 회피(직접 → 토치 들어올림 → 접힘 자세 경유 → 마스트 상승), 통계 |
| `Timeline.cs` | 결정론적 타임라인(갠트리 이동, 관절 보간, 접근, 아크 시작, 용접, 크레이터, 후퇴). 어떤 시각이든 바로 계산 |
| `AppController.cs` | 작업 불러오기. 플레이 모드에서는 계획을 백그라운드 스레드(`Task.Run`)에서 계산, 진행률 표시, 작업을 바꾸면 이전 계산 취소 |
| `WeldSimRunner.cs` | 플레이 모드 재생(속도 배율), 갠트리·로봇·이펙트·비드 반영 |
| `BeadRenderer.cs` + `Shaders/WeldBead.shader` | 용접선마다 비드 메시. 셰이더가 시뮬레이션 시각으로 비드를 늘리고 색을 바꿈(열/자세/이음 종류) |
| `GantryRig.cs`, `RobotArm.cs`, `WeldEffects.cs`, `FactoryCutaway.cs` | 씬에 붙는 컴포넌트 (MonoBehaviour는 파일 이름 = 클래스 이름이어야 씬에 저장됨) |
| `SceneBuilders.cs` | 공장(벽·지붕·기둥·정반·레일·천장크레인), 포털 갠트리, 로봇 모델, 스파크·아크 조명, 카메라 쪽 벽/지붕 숨김 |
| `CameraRig.cs` | 궤도 카메라(전체 보기 / 토치 추적), UI 패널 오른쪽 영역에 화면 중심 맞춤 |
| `UI/WeldingBotUI.cs`, `UI/Loc.cs` | UI Toolkit 왼쪽 패널, 한국어/영어 |
| `Editor/WeldingBotCommands.cs` | CLI 명령 |

## 2. 좌표와 치수
- 공장 54 × 28 m, 처마 12 m. 정반 윗면 y = 0.40 m (44 × 12.4 m)
- 갠트리: 바닥 레일 z = ±9, 거더 y ≈ 8.3 m. 로봇 베이스 범위 X −20~20, Y 1.6~6.8, Z −7~7. 속도 X/Z 0.5 m/s, Y 0.25 m/s
- 로봇: d1 0.50, a1 0.20, 상완 0.85, 전완 0.90, 손목→플랜지 0.12, 토치 0.38 m (도달 1.75 m + 토치)
- 로봇은 `Euler(0,0,180)`으로 거꾸로 장착 (로봇 +Y = 바닥 방향)
- 용접 속도(mm/s): 아래보기 8, 수평 7, 수직 3.5, 위보기 4.5, 맞대기 ×0.8. 아크 시작·크레이터 0.6 s, 접근 10 cm

## 3. 작업 프리셋
| 이름 | 내용 |
|---|---|
| Stiffened Panel | 갑판 2장 맞대기 + 보강재 4개 양쪽 필릿 |
| Inclined Webs | 0°, 20°, −30°, 40° 기운 웹 + 대각선 웹 |
| Angled Panels | 산형(30°), 골형(25°), 20° 경사 패널 위 맞대기 |
| Box Block | 벽 2개(0.8 m), 격벽, 바닥 보강재: 수평·수직 필릿, 교차 구간 분할 |
| Assembly Line | 위 4개를 정반 위에 나란히 (갠트리 장거리 이동) |

## 4. CLI 명령 (`AgentTools\wb.ps1 <명령>`)
- 장면: `wb_setup_scene [--job]`, `wb_load_job --job`, `wb_list_jobs`, `wb_setup_font`
- 분석(씬 변경 없음): `wb_seams --job`, `wb_plan --job`, `wb_sim_run --job all` (검증 포함), `wb_ik_test`
- 플레이 모드 비동기 불러오기: `wb_load_job_async --job [--wait_ms]` (UI와 같은 경로)
- 미리보기(에디트/플레이): `wb_preview --t 0.5`, `wb_seek_seam --seam S08 --f 0.5`, `wb_camera`, `wb_color_mode`
- 플레이 모드: `wb_sim_start --speed`, `wb_sim_pause`, `wb_sim_reset`, `wb_sim_speed`, `wb_sim_advance`, `wb_sim_status`
- 도구: `snap.ps1 <이름> [w] [h] [camera|screen]`, `focus_unity.ps1`, `hashes.ps1`, `SetLang.cs`

## 5. 검증 기준 (2026-10-06)
- `wb_ik_test`: 2000/2000 원래 관절값 복원, 위치 오차 0.001 mm
- `wb_sim_run --job all`: 모든 작업 도달 불가 0, 용접 경로 충돌 0, 공중 이동 충돌 0, TCP 오차 < 1 mm, 갠트리 범위 내
- 계획 계산 시간: Stiffened 0.6 s, Box Block 2.6 s, Assembly Line 5.2 s (백그라운드라 화면은 멈추지 않음)
- 플레이 모드: 작업 전환, 시작/일시정지/초기화, 속도 1~500x, 색 모드 3종, 카메라 2종, 한/영 전환 확인

## 6. 충돌 검사 방식
- Unity Collider/Physics를 쓰지 않고, 로봇 링크를 캡슐(반지름 0.20/0.12/0.09/0.07 m, 토치 0.025 m)로, 판재를 회전된 상자로 보고 직접 계산합니다.
  - 메인 스레드가 아니어도 돌아가므로 백그라운드 계획이 가능하고, 같은 입력이면 항상 같은 결과가 나옵니다.
- 계획할 때는 1 cm 여유를 더 두고(토치 제외), 검증(`wb_sim_run`)은 여유 없이 다시 검사합니다.
- 공중 이동은 도구 이동 약 1 cm마다 검사합니다 (관절 2°마다 검사하면 얇은 판을 건너뛰는 경우가 있었음).
- 공중 이동 회피 순서: 직접 → 토치를 위로 들어 올렸다 이동 → 접힘 자세 경유 → 마스트를 올려 팔 전체를 판재 위로 뺀 뒤 자세 변경 후 내림.

## 7. 알려진 한계
- 갠트리·로봇 본체끼리의 충돌, 케이블, 용접 품질(용입·변형)은 계산하지 않습니다.
- 토치는 곧은 형태(굽은 넥 없음).
