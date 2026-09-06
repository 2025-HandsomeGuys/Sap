using UnityEngine;

namespace Relic
{
    // mp3 리듬 패턴 표. 한 마디(4박) 안에서 노트가 놓이는 위치를 '박 단위 오프셋'으로 적는다.
    //
    // 시간(초)이 아니라 박으로 적는 이유: BPM을 바꿔도 패턴이 그대로 유지되고,
    // 곡 템포와 무관하게 "정박 / 8분 / 셋잇단"이라는 리듬 정체성이 보존된다.
    //
    // MinGapBeats는 전체 패턴에서 가장 좁은 간격(셋잇단 1/3박)이다.
    // 스윙 쿨다운이 이보다 크면 플레이어가 칠 수 없는 노트가 생기므로,
    // Mp3Relic이 이 값을 기준으로 쿨다운을 조인다 — 패턴을 추가할 땐 이 상수도 함께 확인할 것.
    public static class Mp3BeatPatterns
    {
        public const int MeasureBeats = 4;
        public const float MinGapBeats = 1f / 3f;   // 셋잇단

        private const float T3 = 1f / 3f;   // 셋잇단 한 칸
        private const float T6 = 2f / 3f;   // 셋잇단 두 칸

        // 각 배열 = 한 마디분 노트 위치(0 이상 MeasureBeats 미만, 오름차순).
        public static readonly float[][] All =
        {
            new[] { 0f, 1f, 2f, 3f },                          // 정박 — 기준 리듬
            new[] { 0f, 1f, 3f },                              // 한 박 쉼
            new[] { 0f, 2f },                                  // 성기게(2박)
            new[] { 0f, 0.5f, 1f, 2f, 2.5f, 3f },              // 2번 치고 쉬고
            new[] { 0f, 1f, 1.5f, 2f, 3f, 3.5f },              // 뒤에 붙는 8분
            new[] { 0f, 0.5f, 1.5f, 2f, 3f },                  // 당김음
            new[] { 0f, T3, T6, 1f, 2f, 3f },                  // 첫 박 셋잇단
            new[] { 0f, 1f, 2f, 2f + T3, 2f + T6, 3f },        // 셋째 박 셋잇단
            new[] { 0f, T3, T6, 2f, 2f + T3, 2f + T6 },        // 셋잇단 2회
            new[] { 0f, 0.5f, 1f, 1.5f, 2f, 3f },              // 몰아치고 쉼
        };

        public static float[] Random() => All[UnityEngine.Random.Range(0, All.Length)];
    }
}
