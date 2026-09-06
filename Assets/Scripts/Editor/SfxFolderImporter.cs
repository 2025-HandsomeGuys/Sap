// @tags: sound, sfx, editor, tool, import, sounddata
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Audio/SFX/ 안의 오디오 파일을 스캔해 파일명(확장자 제외)을 그대로 키로 삼아
/// SoundData.asset의 sfxClips에 등록한다.
///
/// 40여 개를 인스펙터로 손수 넣는 건 고역이고, 오타가 나면 무음이라 알아채기 어렵다.
/// 음원을 받아 파일명만 SfxKeys의 값과 맞춰 폴더에 넣고 이 메뉴를 누르면 끝난다.
///
/// 규칙:
///  - 같은 키가 이미 있으면 클립 참조만 갱신한다(중복 추가 없음).
///  - 폴더에 없는데 에셋에만 남은 키는 삭제하지 않는다(수동 등록분 보호). 경고만 띄운다.
///  - SfxKeys.All에 있는데 폴더에 파일이 없는 키를 콘솔에 나열한다
///    → 어떤 소리가 아직 안 채워졌는지 한눈에 본다.
/// </summary>
public static class SfxFolderImporter
{
    private const string SfxFolder = "Assets/Audio/SFX";

    // SoundManager가 Resources.Load<SoundDataSO>("SoundData")로 읽는 바로 그 에셋이어야 한다.
    // 다른 데 있는 SoundData를 편집하면 게임이 안 읽는 파일을 고치는 셈이 된다.
    private const string CanonicalPath = "Assets/Resources/SoundData.asset";

    [MenuItem("Tools/Sound/Rescan SFX Folder")]
    public static void Rescan()
    {
        var soundData = ResolveSoundData();
        if (soundData == null) return;

        if (!Directory.Exists(SfxFolder))
        {
            Debug.LogError($"[SfxFolderImporter] 폴더가 없다: {SfxFolder}\n" +
                           "폴더를 만들고 음원을 넣은 뒤 다시 실행할 것.");
            return;
        }

        // 1) 폴더 스캔 — 파일명(확장자 제외) → 클립
        //
        // 최상위만 본다. FindAssets는 재귀 검색이라, 에셋 팩(kenney 등)을 하위 폴더에
        // 풀어놓으면 수백 개가 쓰레기 키로 등록된다. 하위 폴더는 "원본 보관소"로 두고,
        // 실제로 쓸 것만 최상위에 키 이름으로 복사하는 구조다.
        var found = new Dictionary<string, AudioClip>();
        int skippedInSubfolders = 0;

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SfxFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            string dir = Path.GetDirectoryName(path);
            if (dir != null) dir = dir.Replace('\\', '/');
            if (dir != SfxFolder) { skippedInSubfolders++; continue; }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            string key = Path.GetFileNameWithoutExtension(path);
            if (found.ContainsKey(key))
            {
                Debug.LogWarning($"[SfxFolderImporter] 키 중복: '{key}' — {path} 를 건너뛴다.\n" +
                                 "확장자만 다른 같은 이름의 파일이 있는지 확인할 것(예: .mp3와 .ogg).");
                continue;
            }
            found[key] = clip;
        }

        // 2) SoundData에 반영
        var so = new SerializedObject(soundData);
        SerializedProperty list = so.FindProperty("sfxClips");

        int updated = 0, added = 0;
        foreach (var kv in found)
        {
            int index = IndexOfKey(list, kv.Key);
            if (index >= 0)
            {
                var elem = list.GetArrayElementAtIndex(index);
                var clipProp = elem.FindPropertyRelative("audioClip");
                if (clipProp.objectReferenceValue != kv.Value)
                {
                    clipProp.objectReferenceValue = kv.Value;
                    updated++;
                }
            }
            else
            {
                list.arraySize++;
                var elem = list.GetArrayElementAtIndex(list.arraySize - 1);
                elem.FindPropertyRelative("soundName").stringValue = kv.Key;
                elem.FindPropertyRelative("audioClip").objectReferenceValue = kv.Value;
                added++;
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(soundData);
        AssetDatabase.SaveAssets();

        // 3) 리포트
        Debug.Log($"[SfxFolderImporter] 완료 — 신규 {added}개, 갱신 {updated}개 " +
                  $"(폴더에서 찾은 클립 {found.Count}개" +
                  (skippedInSubfolders > 0 ? $", 하위 폴더 {skippedInSubfolders}개 무시" : "") + ")");

        var missing = SfxKeys.All.Where(k => !found.ContainsKey(k)).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogWarning($"[SfxFolderImporter] 아직 음원이 없는 키 {missing.Length}개:\n" +
                             "  " + string.Join("\n  ", missing));
        }
        else
        {
            Debug.Log("[SfxFolderImporter] SfxKeys의 모든 키에 음원이 있다.");
        }

        var orphan = new List<string>();
        for (int i = 0; i < list.arraySize; i++)
        {
            string key = list.GetArrayElementAtIndex(i).FindPropertyRelative("soundName").stringValue;
            if (!found.ContainsKey(key)) orphan.Add(key);
        }
        if (orphan.Count > 0)
        {
            Debug.LogWarning($"[SfxFolderImporter] 폴더에 없는데 SoundData에만 있는 키 " +
                             $"{orphan.Count}개(수동 등록분일 수 있어 삭제하지 않았다):\n" +
                             "  " + string.Join(", ", orphan));
        }
    }

    /// <summary>
    /// 편집할 SoundDataSO를 찾는다.
    ///
    /// 정규 위치(Resources)를 먼저 보고, 없으면 프로젝트 전체에서 타입으로 검색한다.
    /// 경로를 하드코딩하면 에셋을 옮겼을 때 조용히 깨진다(실제로 한 번 깨졌다).
    /// 단, Resources 밖에 있으면 SoundManager가 못 읽으므로 경고한다.
    /// </summary>
    private static SoundDataSO ResolveSoundData()
    {
        var soundData = AssetDatabase.LoadAssetAtPath<SoundDataSO>(CanonicalPath);
        if (soundData != null) return soundData;

        string[] guids = AssetDatabase.FindAssets("t:SoundDataSO");
        if (guids.Length == 0)
        {
            Debug.LogError("[SfxFolderImporter] SoundDataSO 에셋을 찾지 못했다.\n" +
                           $"프로젝트에 하나도 없다면 만들고, {CanonicalPath} 위치에 둘 것.");
            return null;
        }

        if (guids.Length > 1)
        {
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath);
            Debug.LogError("[SfxFolderImporter] SoundDataSO가 여러 개다. 어느 것을 편집할지 알 수 없다:\n  " +
                           string.Join("\n  ", paths) +
                           $"\n하나만 남기고 {CanonicalPath}에 둘 것.");
            return null;
        }

        string found = AssetDatabase.GUIDToAssetPath(guids[0]);
        Debug.LogWarning($"[SfxFolderImporter] SoundData가 정규 위치에 없다: {found}\n" +
                         "SoundManager는 Resources.Load(\"SoundData\")로 읽으므로, " +
                         $"Resources 폴더 밖에 있으면 게임이 이 에셋을 못 읽는다. {CanonicalPath}로 옮길 것.");
        return AssetDatabase.LoadAssetAtPath<SoundDataSO>(found);
    }

    private static int IndexOfKey(SerializedProperty list, string key)
    {
        for (int i = 0; i < list.arraySize; i++)
        {
            var name = list.GetArrayElementAtIndex(i).FindPropertyRelative("soundName");
            if (name != null && name.stringValue == key) return i;
        }
        return -1;
    }
}
