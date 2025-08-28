using UnityEngine;

public class DatabaseLoader : MonoBehaviour
{
    // Inspector창에서 ItemDatabase.asset 파일을 이 필드에 할당해주세요.
    // 이 참조가 존재함으로써 Unity는 씬이 로드될 때 ItemDatabase 에셋을 메모리에 로드하고,
    // ItemDatabase.Instance가 설정되도록 보장합니다.
    public ItemDatabase itemDatabase;
}
