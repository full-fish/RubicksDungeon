using UnityEngine;
using System;

// 행동 종류 정의
public enum ActionType { None, Move, ShiftRow, ShiftCol }

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

public class GameState
{
    public int[,,] mapData;
    public Vector2Int playerPos;
    public int remainingShifts;
    public Quaternion playerRot;

    // ★ [추가] 역재생을 위한 행동 정보
    public ActionType actionType;
    public int val1; // dx 또는 index
    public int val2; // dy 또는 dir

    public GameState(int[,,] map, Vector2Int pos, int shifts, Quaternion rot, ActionType type, int v1, int v2) 
    {
        this.mapData = map;
        this.playerPos = pos;
        this.remainingShifts = shifts;
        this.playerRot = rot;
        
        this.actionType = type;
        this.val1 = v1;
        this.val2 = v2;
    }
}

public enum SoundType { Walk, Push, Destroy }