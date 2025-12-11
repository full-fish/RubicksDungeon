using UnityEngine;
using System;

// 스테이지 JSON 데이터 구조
[Serializable]
public class StageDataRoot
{
    public StageProps properties;
    public StageLayers layers;
}

[Serializable]
public class StageProps
{
    public string stageName;
    public int maxShifts;
    public int width;
    public int height;
}

[Serializable]
public class StageLayers
{
    public int[][] tile;   
    public int[][] ground; 
    public int[][] sky;    
}

// Undo(뒤로가기) 저장용 상태
public class GameState
{
    public int[,,] mapData;
    public Vector2Int playerPos;
    public int remainingShifts;
    public Quaternion playerRot;

    public GameState(int[,,] map, Vector2Int pos, int shifts, Quaternion rot) 
    {
        this.mapData = map;
        this.playerPos = pos;
        this.remainingShifts = shifts;
        this.playerRot = rot; 
    }
}

// 오디오 타입 열거형
public enum SoundType { Walk, Push, Destroy }