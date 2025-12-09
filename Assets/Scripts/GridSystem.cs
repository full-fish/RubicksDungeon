using UnityEngine;
using System;

public class GridSystem : MonoBehaviour
{
    private int width;
    private int height;
    private int[,] mapData; // [ID + 모양번호] 패킹된 데이터
    private TileData[] tilePalette;
    
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

        // ID에 랜덤 모양 번호(0~99)를 섞어서 저장
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

    // --- 데이터 조회용 ---
    public int GetTileID(int x, int y)
    {
        if (!IsInMap(x, y)) return -1;
        return mapData[x, y] & 0xFFFF; // 하위 16비트 (ID)
    }

    public int GetTileVariant(int x, int y)
    {
        if (!IsInMap(x, y)) return 0;
        return (mapData[x, y] >> 16) & 0xFFFF; // 상위 16비트 (모양 번호)
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

    // --- [1] 플레이어 이동 (1개만 밀기) ---
    public void TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;

        if (!IsInMap(nextX, nextY)) return;

        TileData nextTile = GetTileDataFromPacked(mapData[nextX, nextY]);

        // 상자 밀기 시도
        if (nextTile != null && nextTile.isPush)
        {
            int pushX = nextX + dx;
            int pushY = nextY + dy;

            if (!IsInMap(pushX, pushY)) return;

            TileData afterBox = GetTileDataFromPacked(mapData[pushX, pushY]);
            
            // 상자 뒤가 비어있거나, 장애물(Stop/Push)이 없어야 밀림
            bool canPush = (afterBox == null) || (!afterBox.isStop && !afterBox.isPush);

            if (canPush)
            {
                mapData[pushX, pushY] = mapData[nextX, nextY]; // 데이터 이동
                mapData[nextX, nextY] = 0; // 내 자리는 0(바닥)으로
                OnMapChanged?.Invoke();
            }
            else return; // 뒤가 막힘
        }
        // 벽이면 이동 불가
        else if (nextTile != null && nextTile.isStop)
        {
            return;
        }

        PlayerIndex = new Vector2Int(nextX, nextY);
        OnPlayerMoved?.Invoke();
        CheckFoot();
    }

    // --- [2] 맵 회전 (WASD) ---
    public void TryPushRow(int dir)
    {
        if (!CanPushRow(PlayerIndex.y)) return;
        ShiftRow(PlayerIndex.y, -dir);
        ResolveCollision(-dir, true);
    }

    public void TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x)) return;
        ShiftCol(PlayerIndex.x, -dir);
        ResolveCollision(-dir, false);
    }

    // --- [3] 충돌 해결 (벽은 플레이어를 밀고, 상자는 역주행) ---
    void ResolveCollision(int dir, bool isRow)
    {
        int packedData = mapData[PlayerIndex.x, PlayerIndex.y];
        TileData incomingTile = GetTileDataFromPacked(packedData);

        if (incomingTile == null) { CheckFoot(); return; }

        // A. 벽이 덮침 -> 플레이어 밀려남
        if (incomingTile.isStop)
        {
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            PlayerIndex = new Vector2Int(newX, newY); // 좌표 수정
            OnPlayerMoved?.Invoke();
        }
        // B. 상자가 덮침 -> 플레이어가 버티고 상자를 뒤로 밀어냄
        else if (incomingTile.isPush)
        {
            mapData[PlayerIndex.x, PlayerIndex.y] = 0; // 내 자리는 비움

            int boxToSave = packedData;
            int checkX = PlayerIndex.x;
            int checkY = PlayerIndex.y;
            int loopCount = isRow ? width : height;

            // 역주행하며 빈 공간 찾기
            for (int i = 0; i < loopCount; i++)
            {
                if (isRow) checkX = GetWrappedIndex(checkX - dir, width);
                else       checkY = GetWrappedIndex(checkY - dir, height);

                int prevData = mapData[checkX, checkY];
                TileData prevTile = GetTileDataFromPacked(prevData);

                if (prevTile != null && prevTile.isStop) break; // 뒤가 벽이면 상자 파괴
                else if (prevTile == null || (!prevTile.isStop && !prevTile.isPush))
                {
                    mapData[checkX, checkY] = boxToSave; // 빈 곳에 안착
                    break;
                }
                else if (prevTile.isPush)
                {
                    mapData[checkX, checkY] = boxToSave; // 교체하고 계속 뒤로 감
                    boxToSave = prevData;
                }
            }
            OnMapChanged?.Invoke();
        }
        CheckFoot();
    }

    // --- 보조 함수 ---
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
            if (t != null && !t.isShift) return false; // 고정축 발견 시 회전 불가
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