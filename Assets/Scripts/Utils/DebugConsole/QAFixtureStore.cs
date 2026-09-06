// @tags: qa, fixture, save, debug, console, balance, snapshot
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DebugTools
{
    public struct QAFixtureInfo
    {
        public string name;
        public string memo;        // meta.txt 첫 줄
        public bool hasWorld;      // 파진 지형 포함 여부
        public DateTime savedAt;
    }

    /// <summary>
    /// QA 픽스처 저장소 — 파일 조작만 담당한다(매니저·씬을 모른다).
    ///
    /// ⚠ 픽스처가 파일 '두 개' 세트인 이유.
    ///   이 게임의 저장 상태는 두 곳에 나뉘어 있다.
    ///     playerData_{슬롯}.json — 골드·스탯·퀘스트·코인·업그레이드 (SaveManager)
    ///     worldData.bin          — 파진 지형 (WorldPersistenceSystem)
    ///   그리고 worldData.bin에는 <b>슬롯 인덱스가 없다</b> — 5개 슬롯이 지형 하나를 공유한다.
    ///   그래서 세이브 슬롯만으로는 "그 상황"을 보관할 수가 없다(슬롯을 바꿔도 굴은 그대로 따라온다).
    ///   픽스처는 두 파일을 한 폴더에 함께 굽고 함께 되돌린다.
    ///
    /// 픽스처에 worldData.bin이 없으면 복원 시 살아 있는 worldData.bin을 <b>지운다</b>.
    ///   안 지우면 직전에 플레이하던 굴이 그대로 남아 "지형이 안 파진 1일차" 픽스처가 거짓말이 된다.
    /// </summary>
    public static class QAFixtureStore
    {
        public const string PlayerFileName = "playerData.json";
        public const string WorldFileName = "worldData.bin";
        public const string MetaFileName = "meta.txt";

        private const string BackupFolder = "_backup";

        /// <summary>
        /// 에디터에서는 Assets/ 아래 — UVCS가 관리해서 재설치·다른 머신에서도 남는다.
        /// 빌드에서는 Assets 폴더가 없으므로 persistentDataPath로 떨어진다(그 빌드 안에서만 유효).
        /// </summary>
        public static string Root =>
#if UNITY_EDITOR
            Path.Combine(Application.dataPath, "QA", "Fixtures");
#else
            Path.Combine(Application.persistentDataPath, "QA", "Fixtures");
#endif

        public static string LiveWorldPath =>
            Path.Combine(Application.persistentDataPath, WorldFileName);

        public static string LivePlayerPath(int slot) =>
            Path.Combine(Application.persistentDataPath, $"playerData_{slot}.json");

        public static string PathOf(string name) => Path.Combine(Root, name);

        // ───────────────────────────────────────────────────────────

        public static List<QAFixtureInfo> List()
        {
            var result = new List<QAFixtureInfo>();
            if (!Directory.Exists(Root)) return result;

            foreach (string dir in Directory.GetDirectories(Root))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("_")) continue;   // _backup 등 내부 폴더

                string playerPath = Path.Combine(dir, PlayerFileName);
                if (!File.Exists(playerPath)) continue;

                result.Add(new QAFixtureInfo
                {
                    name = name,
                    memo = ReadMemo(dir),
                    hasWorld = File.Exists(Path.Combine(dir, WorldFileName)),
                    savedAt = File.GetLastWriteTime(playerPath),
                });
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        /// <summary>
        /// 현재 디스크 상태를 픽스처로 굽는다.
        /// 호출 전에 게임 상태가 디스크에 내려가 있어야 한다(SaveManager.Save + InfinityMapManager.SaveAllData).
        /// </summary>
        public static string Capture(string name, int slot, string metaText)
        {
            string error = ValidateName(name);
            if (error != null) return error;

            string src = LivePlayerPath(slot);
            if (!File.Exists(src))
                return $"슬롯 {slot}의 세이브 파일이 없다: {src}";

            string dir = PathOf(name);
            Directory.CreateDirectory(dir);

            File.Copy(src, Path.Combine(dir, PlayerFileName), true);

            // 지형은 없을 수 있다(지하에 한 번도 안 내려간 상태). 없으면 이전 픽스처의 잔재를 지운다.
            string worldDst = Path.Combine(dir, WorldFileName);
            if (File.Exists(LiveWorldPath)) File.Copy(LiveWorldPath, worldDst, true);
            else if (File.Exists(worldDst)) File.Delete(worldDst);

            File.WriteAllText(Path.Combine(dir, MetaFileName), metaText ?? "", Encoding.UTF8);

            RefreshAssetDatabase();
            return null;
        }

        /// <summary>
        /// 픽스처를 실제 세이브 위치로 되돌린다. 씬 재시작은 호출측 책임.
        /// 덮어쓰기 전 현재 상태를 <c>_backup/</c>에 남긴다 — 실수로 진행 상황을 날리지 않도록.
        /// </summary>
        public static string Restore(string name, int slot)
        {
            string dir = PathOf(name);
            string playerSrc = Path.Combine(dir, PlayerFileName);
            if (!File.Exists(playerSrc)) return $"그런 픽스처 없음: {name}";

            BackupLive(slot);

            File.Copy(playerSrc, LivePlayerPath(slot), true);

            string worldSrc = Path.Combine(dir, WorldFileName);
            if (File.Exists(worldSrc))
            {
                File.Copy(worldSrc, LiveWorldPath, true);
            }
            else if (File.Exists(LiveWorldPath))
            {
                // 픽스처에 지형이 없다 = "아직 아무것도 안 판 상태"가 정답이다.
                File.Delete(LiveWorldPath);
            }

            return null;
        }

        public static string Delete(string name)
        {
            string dir = PathOf(name);
            if (!Directory.Exists(dir)) return $"그런 픽스처 없음: {name}";

            Directory.Delete(dir, true);
            RefreshAssetDatabase();
            return null;
        }

        public static string BackupPath => Path.Combine(Root, BackupFolder);

        // ───────────────────────────────────────────────────────────

        private static void BackupLive(int slot)
        {
            string dir = BackupPath;
            Directory.CreateDirectory(dir);

            string player = LivePlayerPath(slot);
            if (File.Exists(player)) File.Copy(player, Path.Combine(dir, PlayerFileName), true);

            if (File.Exists(LiveWorldPath))
                File.Copy(LiveWorldPath, Path.Combine(dir, WorldFileName), true);
            else
            {
                string stale = Path.Combine(dir, WorldFileName);
                if (File.Exists(stale)) File.Delete(stale);
            }
        }

        private static string ReadMemo(string dir)
        {
            string metaPath = Path.Combine(dir, MetaFileName);
            if (!File.Exists(metaPath)) return "";

            try
            {
                foreach (string line in File.ReadAllLines(metaPath, Encoding.UTF8))
                    if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QAFixture] meta 읽기 실패 ({dir}): {e.Message}");
            }
            return "";
        }

        private static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "이름이 비었다";

            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                       || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return $"이름에 쓸 수 없는 문자: '{c}'  (영문·숫자·_·- 만)";
            }

            if (name.StartsWith("_")) return "'_'로 시작하는 이름은 내부 폴더용이라 못 쓴다";
            return null;
        }

        private static void RefreshAssetDatabase()
        {
#if UNITY_EDITOR
            // Assets/ 아래에 파일을 만들었으므로 Unity·UVCS가 알아채게 한다.
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
    }
}
#endif
