using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot.UI
{
    /// <summary>Tiny localisation table: English text is the key, Korean is looked up.</summary>
    public static class Loc
    {
        const string PrefKey = "wb_lang";
        static bool? korean;
        public static event System.Action Changed;

        public static bool Korean
        {
            get { if (korean == null) korean = PlayerPrefs.GetInt(PrefKey, 1) == 1; return korean.Value; }
            set { korean = value; PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        static readonly Dictionary<string, string> Ko = new Dictionary<string, string>
        {
            ["WeldingBot"] = "WeldingBot",
            ["Shipyard welding robot simulator"] = "조선소 용접 로봇 시뮬레이터",
            ["Job"] = "작업 선택",
            ["Start"] = "작업 시작",
            ["Pause"] = "일시정지",
            ["Resume"] = "재개",
            ["Reset"] = "초기화",
            ["Simulation speed"] = "시뮬레이션 속도",
            ["Bead colour"] = "용접 표시 색",
            ["Heat"] = "열(냉각)",
            ["Position"] = "용접 자세",
            ["Joint type"] = "이음 종류",
            ["Camera"] = "카메라",
            ["Overview"] = "전체 보기",
            ["Follow torch"] = "토치 추적",
            ["Summary"] = "요약",
            ["Seams"] = "용접선 목록",
            ["Language"] = "언어",
            ["Progress"] = "진행",
            ["Idle"] = "대기",
            ["Park"] = "로봇 대기 자세로",
            ["GantryMove"] = "갠트리 이동",
            ["AirMove"] = "로봇 이동",
            ["Approach"] = "접근",
            ["ArcStart"] = "아크 시작",
            ["Weld"] = "용접 중",
            ["Crater"] = "크레이터 처리",
            ["Retract"] = "후퇴",
            ["Finished"] = "작업 완료",
            ["Ready"] = "준비",
            ["Planning"] = "경로 계획 중",
            ["Planning gantry stations and robot paths..."] = "갠트리 위치와 로봇 경로를 계산하는 중...",
            ["Gantry lifts"] = "마스트 상승 회피",
            ["Total time"] = "총 작업 시간",
            ["Arc time"] = "아크 시간",
            ["Arc-on ratio"] = "아크 가동률",
            ["Welded length"] = "용접 길이",
            ["Seams done"] = "완료 용접선",
            ["Unreachable"] = "도달 불가",
            ["Gantry moves"] = "갠트리 이동 횟수",
            ["Gantry travel"] = "갠트리 이동 거리",
            ["Gantry time"] = "갠트리 이동 시간",
            ["Fixed stations"] = "고정 스테이션 수",
            ["Tracked seams"] = "갠트리 추적 용접",
            ["Robot air time"] = "로봇 공중 이동",
            ["Wire used"] = "와이어 사용량",
            ["Flat"] = "아래보기",
            ["Horizontal"] = "수평",
            ["Vertical"] = "수직",
            ["Overhead"] = "위보기",
            ["Fillet"] = "필릿",
            ["Butt"] = "맞대기",
            ["Planned time"] = "계획 작업 시간",
            ["Plan computed in"] = "계획 계산 시간",
            ["By position"] = "자세별 길이",
            ["By joint"] = "이음별 길이",
            ["{0} seams: {1} fillet, {2} butt, {3:0.0} m"] = "용접선 {0}개: 필릿 {1}, 맞대기 {2}, 총 {3:0.0} m",
            ["{0} unreachable"] = "도달 불가 {0}개",
            ["all seams reachable"] = "모든 용접선 도달 가능",
            ["fixed"] = "고정",
            ["track"] = "추적",
            ["n/a"] = "불가",
            ["Stiffened Panel"] = "보강 패널",
            ["Inclined Webs"] = "경사 웹",
            ["Angled Panels"] = "각도 판재",
            ["Box Block"] = "박스 블록",
            ["Assembly Line"] = "전체 라인 (4개 작업)",
            ["Two deck plates butt-welded, four stiffeners fillet-welded on both sides."] = "갑판 두 장을 맞대기 용접하고, 보강재 4개를 양쪽 필릿 용접합니다.",
            ["Webs leaning 0-40 degrees and a diagonal web: torch angle follows the joint."] = "0~40° 기울어진 웹과 대각선 웹: 이음 각도에 맞춰 토치 각도가 바뀝니다.",
            ["Edge-to-edge joints at angles: ridge, valley and a butt on an inclined panel."] = "각도를 이룬 판재 맞대기: 산형, 골형, 경사 패널 위 맞대기.",
            ["Box with walls, bulkhead and a floor stiffener: horizontal and vertical fillets."] = "벽, 격벽, 바닥 보강재가 있는 박스: 수평·수직 필릿 용접.",
            ["All four jobs along the platen: the gantry travels between work areas."] = "네 가지 작업을 정반 위에 나란히 배치: 갠트리가 작업 구역 사이를 이동합니다.",
            ["Left drag: rotate · Right drag: pan · Wheel: zoom"] = "왼쪽 드래그: 회전 · 오른쪽 드래그: 이동 · 휠: 확대/축소",
        };

        public static string T(string en)
        {
            if (!Korean || en == null) return en;
            return Ko.TryGetValue(en, out var k) ? k : en;
        }

        public static string F(string en, params object[] args) => string.Format(T(en), args);
    }
}
