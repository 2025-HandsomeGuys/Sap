// @tags: bug-report, qa, serialization, data
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.Collections.Generic;

namespace BugReport
{
    /// <summary>
    /// report.json 최상위 구조. 로직 없는 순수 데이터 컨테이너.
    ///
    /// 좌표를 Vector2Int가 아니라 string으로 담는 이유:
    /// 이 파일의 유일한 용도가 사람이 눈으로 읽는 것이기 때문이다.
    /// </summary>
    [Serializable]
    public class BugReportData
    {
        public string memo = "";
        public BugReportMeta meta = new BugReportMeta();
        public BugReportPlayer player = new BugReportPlayer();
        public BugReportWorld world = new BugReportWorld();
        public string ui = Unavailable;

        /// <summary>확장 컨텍스트. "key=value" 목록 — JsonUtility가 Dictionary를 직렬화하지 못한다.</summary>
        public List<string> extra = new List<string>();

        /// <summary>수집 실패한 필드에 넣는 표식. 이 값이 보여도 리포트 자체는 정상 저장된 것이다.</summary>
        public const string Unavailable = "<unavailable>";
    }

    [Serializable]
    public class BugReportMeta
    {
        public string localTime = "";
        public string appVersion = "";
        public string unityVersion = "";
        public string platform = "";
        public string sceneName = BugReportData.Unavailable;
        public string resolution = "";
        public float realtimeSinceStartup;
        public long totalMemoryMB;
        public int sessionErrorCount;
        public int sessionExceptionCount;
    }

    [Serializable]
    public class BugReportPlayer
    {
        public string worldPos = BugReportData.Unavailable;
        public string chunkCoord = BugReportData.Unavailable;
        public string velocity = BugReportData.Unavailable;
        public string stamina = BugReportData.Unavailable;
        public string gold = BugReportData.Unavailable;
        public string miningLevel = BugReportData.Unavailable;
        public string weight = BugReportData.Unavailable;
    }

    [Serializable]
    public class BugReportWorld
    {
        public string layer = BugReportData.Unavailable;
        public string day = BugReportData.Unavailable;
        public string timeOfDay = BugReportData.Unavailable;
        public string inDungeon = BugReportData.Unavailable;
        public int loadedChunkCount;
        public List<string> loadedChunks = new List<string>();
    }

    /// <summary>로그 링버퍼 한 줄.</summary>
    public struct BugReportLogEntry
    {
        public string condition;
        public string stackTrace;
        public UnityEngine.LogType type;
        public float time;
    }
}
#endif
