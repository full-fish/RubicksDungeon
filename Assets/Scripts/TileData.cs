using UnityEngine;

[CreateAssetMenu(fileName = "New Tile", menuName = "Rubik/Tile Data")]
public class TileData : ScriptableObject
{
    public int tileID;        
    public GameObject prefab; 

    [Header("이동 관련")]
    public bool isStop;       // 이동 불가 (벽)
    
    [Tooltip("플레이어가 몸으로 밀 수 있는가? (상자)")]
    public bool isPush;       // 플레이어가 직접 미는 속성

    [Tooltip("WASD 맵 회전 시 밀리는가? (바닥, 상자, 함정 등)")]
    public bool isShift;      // ★ 추가된 속성: 맵 전체 이동 시 밀림 여부

    [Header("이벤트 관련")]
    public bool isDead;       // 밟으면 죽음
    public bool isGoal;       // 밟으면 클리어

    [Header("속성 관련")]
    public bool isFire;       
    public bool isIce;        
}