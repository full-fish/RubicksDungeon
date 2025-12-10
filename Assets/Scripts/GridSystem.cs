using UnityEngine;
using System;
using System.Collections.Generic;

public class GridSystem : MonoBehaviour
{
    private int width;
    private int height;
    
    // 3차원 배열: [x, y, layer]
    private int[,,] maps; 
    
    private TileData[] tilePalette;
    public Vector2Int PlayerIndex { get; private set; }

    public Action OnMapChanged;
    public Action OnPlayerMoved;
    public Action OnTrapTriggered;
    public Action OnGoalTriggered;
    
    private const int PLAYER_LAYER = 1;

    public void Initialize(int w, int h, int[,,] loadedMaps, TileData[] palette, Vector2Int startPos)
    {
        width = w;
        height = h;
        tilePalette = palette;
        PlayerIndex = startPos;
        maps = loadedMaps; 

        // 패킹
        for (int l = 0; l < 3; l++) {
            for (int x = 0; x < w; x++) {
                for (int y = 0; y < h; y++) {
                    int id = maps[x, y, l];
                    if (id != 0) {
                        int variant = UnityEngine.Random.Range(0, 100);
                        maps[x, y, l] = id | (variant << 16);
                    }
                }
            }
        }
    }

    // --- 데이터 조회 ---
    public int GetLayerID(int x, int y, int layer) {
        if (!IsInMap(x, y)) return 0;
        return maps[x, y, layer] & 0xFFFF; 
    }
    
    public int GetLayerVariant(int x, int y, int layer) {
        if (!IsInMap(x, y)) return 0;
        return (maps[x, y, layer] >> 16) & 0xFFFF; 
    }

    private TileData GetTileDataFromPacked(int packedData) {
        if (packedData == 0) return null;
        int id = packedData & 0xFFFF;
        foreach (var data in tilePalette) if (data.tileID == id) return data;
        return null;
    }

    // --- [1] 플레이어 이동 (직접 이동) ---
    public bool TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;
        
        if (!IsInMap(nextX, nextY)) return false;

        int layer = 1;
        TileData nextObj = GetTileDataFromPacked(maps[nextX, nextY, layer]);

        // 1. 밀기 시도
        if (nextObj != null && nextObj.isPush) {
            int pushX = nextX + dx;
            int pushY = nextY + dy;
            
            if (!IsInMap(pushX, pushY)) return false;

            TileData destObj = GetTileDataFromPacked(maps[pushX, pushY, layer]);
            TileData destFloor = GetTileDataFromPacked(maps[pushX, pushY, 0]);

            // 단순 밀기: 뒤가 비어있고 바닥이 막히지 않아야 함
            // (Shift와 달리 직접 이동 밀기는 보통 하나만 밈, 필요시 Chain 적용 가능)
            bool canPush = (destObj == null) && (destFloor == null || !destFloor.isStop);

            if (canPush) {
                maps[pushX, pushY, layer] = maps[nextX, nextY, layer]; 
                maps[nextX, nextY, layer] = 0; 
                OnMapChanged?.Invoke();
            } else {
                return false; // 밀기 실패 -> 이동 실패
            }
        }
        // 2. 벽 체크 (Push가 아니거나 Push 실패 후)
        else if (IsStopAt(nextX, nextY)) {
            return false;
        }

        PlayerIndex = new Vector2Int(nextX, nextY);
        OnPlayerMoved?.Invoke();
        CheckFoot();
        return true;
    }

    private bool IsStopAt(int x, int y) {
        for (int l = 0; l < 3; l++) {
            TileData t = GetTileDataFromPacked(maps[x, y, l]);
            if (t != null && t.isStop) return true;
        }
        return false;
    }

    // --- [2] 맵 회전 (Shift) ---
    public bool TryPushRow(int dir)
    {
        if (!CanPushRow(PlayerIndex.y)) return false;
        
        ShiftRow(PlayerIndex.y, -dir); // 맵 이동
        
        // 충돌 해결 시도
        if (!ResolveCollision(-dir, true))
        {
            ShiftRow(PlayerIndex.y, dir); // 실패 시 원상복구
            return false;
        }
        return true;
    }

    public bool TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x)) return false;
        
        ShiftCol(PlayerIndex.x, -dir);
        
        if (!ResolveCollision(-dir, false))
        {
            ShiftCol(PlayerIndex.x, dir);
            return false;
        }
        return true;
    }

    // --- [3] 충돌 해결 (핵심 수정됨) ---
    bool ResolveCollision(int dir, bool isRow) {
        if (!HandleLayerCollision(1, dir, isRow)) return false;
        CheckFoot();
        return true;
    }

    bool HandleLayerCollision(int layer, int dir, bool isRow) {
        int packedData = maps[PlayerIndex.x, PlayerIndex.y, layer];
        TileData tile = GetTileDataFromPacked(packedData);

        if (tile == null) return true; // 빈 공간 통과

        bool isBlocked = false;

        // 1. Push 속성 확인
        if (tile.isPush) {
            // 밀 수 있는지 확인 (연쇄 밀기)
            if (TryPushChain(PlayerIndex.x, PlayerIndex.y, dir, isRow, layer)) {
                OnMapChanged?.Invoke();
                return true; // 밀기 성공 -> 플레이어 위치 유지
            } else {
                // ★ 중요: Push 속성이지만 뒤가 막혀서 못 밈 -> 벽으로 취급
                isBlocked = true; 
            }
        }

        // 2. Stop 속성이거나, Push하다가 막힌 경우 (isBlocked)
        if (tile.isStop || isBlocked) {
            // 플레이어가 벽(또는 막힌 상자)에 밀려남 -> 플레이어도 같이 이동
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            // 맵 밖으로 밀려나는지 체크
            if (IsInMap(newX, newY)) {
                PlayerIndex = new Vector2Int(newX, newY);
                OnPlayerMoved?.Invoke();
                return true; // 밀려나기 성공
            } else {
                return false; // 맵 밖으로 밀려서 실패 (Shift 취소)
            }
        }

        // 3. Stop도 아니고 Blocked도 아니면 통과 (장판 등)
        return true;
    }

    // 연쇄 밀기 로직
    // dir: 타일들이 이동해온 방향 (예: -1). 우리는 반대로(+1) 밀어야 함.
    bool TryPushChain(int startX, int startY, int dir, bool isRow, int layer) {
        int loopCount = isRow ? width : height;
        List<Vector2Int> chainCoords = new List<Vector2Int>();
        List<int> chainData = new List<int>();

        int curX = startX;
        int curY = startY;

        // 체인 탐색
        for (int i = 0; i < loopCount; i++) {
            chainCoords.Add(new Vector2Int(curX, curY));
            chainData.Add(maps[curX, curY, layer]);

            // 타일이 dir 방향으로 이동해 왔으므로, 밀어낼 방향은 -dir (반대)
            // 예: dir이 -1(왼쪽)이면, 타일들이 왼쪽으로 쏠림 -> 나는 오른쪽(-(-1)=+1)으로 밀어야 함
            int nextX = curX;
            int nextY = curY;
            
            // Wrapped Index 사용
            if (isRow) nextX = GetWrappedIndex(curX - dir, width);
            else       nextY = GetWrappedIndex(curY - dir, height);

            TileData nextTile = GetTileDataFromPacked(maps[nextX, nextY, layer]);
            TileData floorTile = GetTileDataFromPacked(maps[nextX, nextY, 0]);

            if (nextTile == null) {
                // 빈 칸 발견! (끝)
                if (floorTile != null && floorTile.isStop) return false; // 바닥이 구멍/벽이면 못 밈
                
                chainCoords.Add(new Vector2Int(nextX, nextY));
                break;
            }
            else if (nextTile.isPush) {
                // 또 밀 수 있는 물체 -> 계속 탐색 (Box -> Chest 등)
                curX = nextX;
                curY = nextY;
                continue;
            }
            else if (nextTile.isStop) {
                // 벽 만남 -> 전체 체인 이동 불가
                return false; 
            }
            else {
                // 그 외(통과 가능) -> 빈 칸 취급
                chainCoords.Add(new Vector2Int(nextX, nextY));
                break;
            }
        }

        // 맵이 꽉 차서 빈 곳을 못 찾음
        if (chainCoords.Count == chainData.Count) return false;

        // 이동 실행 (뒤에서부터)
        for (int i = 0; i < chainData.Count; i++) {
            Vector2Int dest = chainCoords[i + 1];
            maps[dest.x, dest.y, layer] = chainData[i];
        }

        // 시작 지점 비우기
        maps[startX, startY, layer] = 0;
        return true;
    }

    // --- 유틸리티 ---
    void CheckFoot() {
        int myLayerData = maps[PlayerIndex.x, PlayerIndex.y, PLAYER_LAYER];
        CheckLayerEvent(myLayerData);
    }
    void CheckLayerEvent(int packedData) {
        TileData t = GetTileDataFromPacked(packedData);
        if (t != null) {
            if (t.isDead) OnTrapTriggered?.Invoke();
            if (t.isGoal) OnGoalTriggered?.Invoke();
        }
    }
    bool CanPushRow(int y) {
        for (int x = 0; x < width; x++)
            for (int l = 0; l < 3; l++) 
                if (IsFixed(x, y, l)) return false;
        return true;
    }
    bool CanPushCol(int x) {
        for (int y = 0; y < height; y++)
            for (int l = 0; l < 3; l++)
                if (IsFixed(x, y, l)) return false;
        return true;
    }
    bool IsFixed(int x, int y, int l) {
        TileData t = GetTileDataFromPacked(maps[x, y, l]);
        return t != null && !t.isShift;
    }
    void ShiftRow(int y, int dir) { for(int l=0; l<3; l++) ShiftArray(l, y, dir, true); OnMapChanged?.Invoke(); }
    void ShiftCol(int x, int dir) { for(int l=0; l<3; l++) ShiftArray(l, x, dir, false); OnMapChanged?.Invoke(); }
    void ShiftArray(int layer, int targetIndex, int dir, bool isRow) {
        if (isRow) {
            int y = targetIndex;
            if (dir == 1) { int l = maps[width - 1, y, layer]; for (int x = width - 1; x > 0; x--) maps[x, y, layer] = maps[x - 1, y, layer]; maps[0, y, layer] = l; } 
            else { int f = maps[0, y, layer]; for (int x = 0; x < width - 1; x++) maps[x, y, layer] = maps[x + 1, y, layer]; maps[width - 1, y, layer] = f; }
        } else {
            int x = targetIndex;
            if (dir == 1) { int l = maps[x, height - 1, layer]; for (int y = height - 1; y > 0; y--) maps[x, y, layer] = maps[x, y - 1, layer]; maps[x, 0, layer] = l; } 
            else { int f = maps[x, 0, layer]; for (int y = 0; y < height - 1; y++) maps[x, y, layer] = maps[x, y + 1, layer]; maps[x, height - 1, layer] = f; }
        }
    }
    bool IsInMap(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
    int GetWrappedIndex(int index, int max) => (index % max + max) % max;
    public int[,,] GetMapSnapshot() => (int[,,])maps.Clone();
    public void RestoreMapData(int[,,] savedMap, Vector2Int savedPlayerIndex)
    {
        maps = savedMap;
        PlayerIndex = savedPlayerIndex;
        OnMapChanged?.Invoke(); 
        OnPlayerMoved?.Invoke();
    }
}