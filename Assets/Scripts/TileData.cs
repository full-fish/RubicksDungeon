using UnityEngine;
using System; // Serializable 사용을 위해 필요

[CreateAssetMenu(fileName = "New Tile", menuName = "Rubik/Tile Data")]
public class TileData : ScriptableObject
{
    public int tileID;

    [Header("비주얼 설정 (비율 조절 가능)")]
    public VisualVariant[] variants; 

    [Tooltip("1이면 매니저 설정 그대로, 2면 2배 높게, 0.5면 절반 높이")]
    public float heightMultiplier = 1.0f;
      

    [Header("이동 관련")]
    public bool isStop;       // 벽 (고정)
    public bool isPush;       // 플레이어가 밀 수 있음 (상자)
    public bool isShift;      // 맵 회전 시 같이 돌아감

    [Header("이벤트 관련")]
    public bool isDead;       // 함정
    public bool isGoal;       // 클리어 지점

    [Header("속성 관련")]
    public bool isFire;       
    public bool isIce;        

    // 확률에 따라 비주얼(프리팹+매터리얼)을 뽑아주는 함수
    public VisualVariant GetVariantByWeight(int randomVal) 
    {
        if (variants == null || variants.Length == 0) return new VisualVariant(); 

        float totalWeight = 0;
        foreach (var v in variants) totalWeight += v.chance;

        float randomPoint = (randomVal / 100f) * totalWeight;

        float currentSum = 0;
        foreach (var v in variants)
        {
            currentSum += v.chance;
            if (randomPoint <= currentSum) return v;
        }
        return variants[0];
    }
}

[Serializable]
public struct VisualVariant
{
    public GameObject prefab;    // FBX 모델 또는 프리팹
    public Material overrideMat; // 덮어씌울 매터리얼 (없으면 None)
    [Range(1, 100)] public float chance; // 확률 가중치
}