using UnityEngine;
using System;

public enum TileLayer { Tile = 0, Ground = 1, Sky = 2 }

[CreateAssetMenu(fileName = "New Tile", menuName = "Game/Tile Data")]
public class TileData : ScriptableObject
{
    public int tileID;

    [Header("비주얼")]
    public VisualVariant[] variants; 

    [Header("속성")]
    public TileLayer layerType; 
    public float heightMultiplier = 1.0f;

    [Header("이동 규칙")]
    public bool isStop;       
    public bool isPush;       
    public bool isShift;      

    [Header("이벤트")]
    public bool isDead;       
    public bool isGoal;       

    [Header("전용 사운드 (비워두면 기본음)")]
    public AudioClip clipStep;    
    public AudioClip clipPush;    
    public AudioClip clipDestroy; 

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