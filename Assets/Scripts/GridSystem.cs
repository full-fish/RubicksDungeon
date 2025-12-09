using UnityEngine;
using System;

public class GridSystem : MonoBehaviour
{
    private int width;
    private int height;
    private int[,] mapData; 
    private TileData[] tilePalette;
    
    // 바닥 ID (100번)
    private const int FLOOR_ID = 100; 

    public Vector2Int PlayerIndex { get; private set; }

    public Action OnMapChanged;
    public Action OnPlayerMoved;
    public Action OnTrapTriggered;
    public Action OnGoalTriggered;

    public void Initialize(int w, int h, int[,] rawIds, TileData[] palette, Vector2Int startPos)
    {
        width = w;
        height = h;
        tilePalette = palette;
        PlayerIndex = startPos;

        mapData = new int[w, h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int id = rawIds[x, y];
                int variant = UnityEngine.Random.Range(0, 100);
                mapData[x, y] = id | (variant << 16);
            }
        }
    }

    public int GetTileID(int x, int y)
    {
        if (!IsInMap(x, y)) return -1;
        return mapData[x, y] & 0xFFFF; 
    }

    public int GetTileVariant(int x, int y)
    {
        if (!IsInMap(x, y)) return 0;
        return (mapData[x, y] >> 16) & 0xFFFF; 
    }

    private TileData GetTileDataFromPacked(int packedData)
    {
        int id = packedData & 0xFFFF;
        foreach (var data in tilePalette)
        {
            if (data.tileID == id) return data;
        }
        return null;
    }

    // ★ [헬퍼 함수] 원래 있던 타일의 모양(Variant)을 유지한 바닥 데이터를 만드는 함수
    private int GetFloorWithVariant(int originalPackedData)
    {
        // 1. 원래 데이터에서 모양 번호(상위 16비트)만 추출 (0xFFFF0000)
        int variantPart = originalPackedData & ~0xFFFF; 
        
        // 2. 바닥 ID(100)와 합침
        return FLOOR_ID | variantPart;
    }

    // --- [1] 플레이어 이동 ---
    public void TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;

        if (!IsInMap(nextX, nextY)) return;

        int nextData = mapData[nextX, nextY]; // 데이터 원본 가져오기
        TileData nextTile = GetTileDataFromPacked(nextData);

        if (nextTile != null && nextTile.isPush)
        {
            int pushX = nextX + dx;
            int pushY = nextY + dy;

            if (!IsInMap(pushX, pushY)) return;

            TileData afterBox = GetTileDataFromPacked(mapData[pushX, pushY]);
            bool canPush = (afterBox == null) || (!afterBox.isStop && !afterBox.isPush);

            if (canPush)
            {
                mapData[pushX, pushY] = mapData[nextX, nextY]; 
                
                // ★ [수정] 상자 모양을 그대로 물려받은 바닥 생성
                mapData[nextX, nextY] = GetFloorWithVariant(nextData); 
                
                OnMapChanged?.Invoke();
            }
            else return;
        }
        else if (nextTile != null && nextTile.isStop)
        {
            return;
        }

        PlayerIndex = new Vector2Int(nextX, nextY);
        OnPlayerMoved?.Invoke();
        CheckFoot();
    }

    // --- [2] 맵 회전 ---
    public void TryPushRow(int dir)
    {
        if (!CanPushRow(PlayerIndex.y)) return;
        ShiftRow(PlayerIndex.y, -dir);
        
        if (!ResolveCollision(-dir, true))
        {
            ShiftRow(PlayerIndex.y, dir);
        }
    }

    public void TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x)) return;
        ShiftCol(PlayerIndex.x, -dir);

        if (!ResolveCollision(-dir, false))
        {
            ShiftCol(PlayerIndex.x, dir);
        }
    }

    // --- [3] 충돌 해결 ---
    bool ResolveCollision(int dir, bool isRow)
    {
        int packedData = mapData[PlayerIndex.x, PlayerIndex.y];
        TileData incomingTile = GetTileDataFromPacked(packedData);

        if (incomingTile == null) { CheckFoot(); return true; }

        if (incomingTile.isStop)
        {
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            if (IsInMap(newX, newY))
            {
                PlayerIndex = new Vector2Int(newX, newY);
                OnPlayerMoved?.Invoke();
                CheckFoot();
                return true;
            }
            else
            {
                Debug.Log("맵 밖 회전 취소");
                return false; 
            }
        }
        else if (incomingTile.isPush)
        {
            // ★ [수정] 내가 버티고 서 있는 자리는 (상자 모양을 물려받은) 바닥이어야 함
            mapData[PlayerIndex.x, PlayerIndex.y] = GetFloorWithVariant(packedData);

            int boxToSave = packedData;
            int checkX = PlayerIndex.x;
            int checkY = PlayerIndex.y;
            int loopCount = isRow ? width : height;

            for (int i = 0; i < loopCount; i++)
            {
                if (isRow) checkX = GetWrappedIndex(checkX - dir, width);
                else       checkY = GetWrappedIndex(checkY - dir, height);

                int prevData = mapData[checkX, checkY];
                TileData prevTile = GetTileDataFromPacked(prevData);

                if (prevTile != null && prevTile.isStop) break;
                else if (prevTile == null || (!prevTile.isStop && !prevTile.isPush))
                {
                    mapData[checkX, checkY] = boxToSave;
                    break;
                }
                else if (prevTile.isPush)
                {
                    mapData[checkX, checkY] = boxToSave;
                    boxToSave = prevData;
                }
            }
            OnMapChanged?.Invoke();
            CheckFoot();
            return true;
        }
        
        CheckFoot();
        return true;
    }

    void CheckFoot()
    {
        TileData t = GetTileDataFromPacked(mapData[PlayerIndex.x, PlayerIndex.y]);
        if (t == null) return;
        if (t.isDead) OnTrapTriggered?.Invoke();
        else if (t.isGoal) OnGoalTriggered?.Invoke();
    }

    bool CanPushRow(int y)
    {
        for (int x = 0; x < width; x++)
        {
            TileData t = GetTileDataFromPacked(mapData[x, y]);
            if (t != null && !t.isShift) return false;
        }
        return true;
    }

    bool CanPushCol(int x)
    {
        for (int y = 0; y < height; y++)
        {
            TileData t = GetTileDataFromPacked(mapData[x, y]);
            if (t != null && !t.isShift) return false;
        }
        return true;
    }

    void ShiftRow(int y, int dir)
    {
        if (dir == 1) {
            int last = mapData[width - 1, y];
            for (int x = width - 1; x > 0; x--) mapData[x, y] = mapData[x - 1, y];
            mapData[0, y] = last;
        } else {
            int first = mapData[0, y];
            for (int x = 0; x < width - 1; x++) mapData[x, y] = mapData[x + 1, y];
            mapData[width - 1, y] = first;
        }
        OnMapChanged?.Invoke();
    }

    void ShiftCol(int x, int dir)
    {
        if (dir == 1) {
            int last = mapData[x, height - 1];
            for (int y = height - 1; y > 0; y--) mapData[x, y] = mapData[x, y - 1];
            mapData[x, 0] = last;
        } else {
            int first = mapData[x, 0];
            for (int y = 0; y < height - 1; y++) mapData[x, y] = mapData[x, y + 1];
            mapData[x, height - 1] = first;
        }
        OnMapChanged?.Invoke();
    }

    bool IsInMap(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
    int GetWrappedIndex(int index, int max) => (index % max + max) % max;
}