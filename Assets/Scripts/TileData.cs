using UnityEngine;
using System;

public enum TileLayer
{
    Tile = 0,   // 바닥 (0층)
    Ground = 1,  // 물체 (1층)
    Sky = 2      // 공중 (2층)
}

[CreateAssetMenu(fileName = "New Tile", menuName = "Rubik/Tile Data")]
public class TileData : ScriptableObject
{
    public int tileID;

    [Header("사운드 설정")]
    public AudioClip clipPush;    // 이 물체가 밀릴 때 소리
    public AudioClip clipDestroy; // 이 물체가 파괴될 때 소리
    public AudioClip clipStep;    // (선택) 이 위를 걸을 때 소리
    [Header("비주얼 설정")]
    public VisualVariant[] variants; 

    // ★ [핵심] 층수 설정 (hasFloorUnder 대체)
    [Header("배치 속성")]
    public TileLayer layerType; 

    [Tooltip("1이면 매니저 설정 그대로, 2면 2배 높게")]
    public float heightMultiplier = 1.0f;

    [Header("이동 관련")]
    public bool isStop;       
    public bool isPush;       
    public bool isShift;      

    [Header("이벤트 관련")]
    public bool isDead;       
    public bool isGoal;       

    [Header("속성 관련")]
    public bool isFire;       
    public bool isIce;        

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
    public GameObject prefab;
    public Material overrideMat;
    public Vector3 rotation;
    [Range(1, 100)] public float chance; 
}