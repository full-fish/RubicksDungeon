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
    // --- [1] 플레이어 이동 (수정됨) ---
// --- [1] 플레이어 이동 (수정됨) ---
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

            // [수정 2] null 여부가 아니라, isStop 속성을 기준으로 판단
            // 물체가 없거나(null), 있어도 지나갈 수 있어야(!isStop) 함
            bool isDestPassable = (destObj == null) || (!destObj.isStop);
            
            // 바닥도 없거나(null), 있어도 막히지 않아야(!isStop) 함
            bool isFloorPassable = (destFloor == null) || (!destFloor.isStop);

            if (isDestPassable && isFloorPassable) {
                // [함정 처리] 도착한 곳의 물체(destObj)가 함정(isDead)이라면?
                // 혹은 도착한 바닥(destFloor)이 함정(isDead)이라면? (필요에 따라 추가)
                bool isTrap = (destObj != null && destObj.isDead) || (destFloor != null && destFloor.isDead);

                if (isTrap)
                {
                    // 함정이면 이동하지 않고 현재 위치의 박스를 '제거' (-1)
                    maps[nextX, nextY, layer] = -1; 
                }
                else
                {
                    // 함정이 아니면 정상 이동
                    maps[pushX, pushY, layer] = maps[nextX, nextY, layer]; 
                    
                    // [수정 1] 원래 있던 자리는 -1 (빈 공간)로 채움
                    maps[nextX, nextY, layer] = -1; 
                }

                OnMapChanged?.Invoke();
            } else {
                return false; // 막혀서(isStop) 못 밈
            }
        }
        // 2. 벽 체크 (Push가 아니거나 Push 실패 후)
        else if (IsStopAt(nextX, nextY)) {
            return false;
        }

        // 3. 플레이어 이동 성공
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
    // 연쇄 밀기 로직 (수정됨: 함정 만나면 상자 소멸)
    bool TryPushChain(int startX, int startY, int dir, bool isRow, int layer) {
        int loopCount = isRow ? width : height;
        List<Vector2Int> chainCoords = new List<Vector2Int>();
        List<int> chainData = new List<int>();

        int curX = startX;
        int curY = startY;

        bool hitTrap = false; // 함정을 만났는지 여부

        // 1. 밀어야 할 체인 탐색
        for (int i = 0; i < loopCount; i++) {
            chainCoords.Add(new Vector2Int(curX, curY));
            chainData.Add(maps[curX, curY, layer]);

            // 다음 좌표 계산 (반대 방향으로 밀어야 함)
            int nextX = curX;
            int nextY = curY;
            if (isRow) nextX = GetWrappedIndex(curX - dir, width);
            else       nextY = GetWrappedIndex(curY - dir, height);

            TileData nextTile = GetTileDataFromPacked(maps[nextX, nextY, layer]);
            TileData floorTile = GetTileDataFromPacked(maps[nextX, nextY, 0]);

            // --- [판정 로직 수정] ---
            
            // A. 다음 칸이 비어있음 (-1 or 0)
            if (nextTile == null) {
                // 바닥이 막힌 곳(isStop)인지 체크
                if (floorTile != null && floorTile.isStop) return false; 
                
                // 바닥이 함정(isDead)이면 상자 소멸 처리
                if (floorTile != null && floorTile.isDead) hitTrap = true;

                chainCoords.Add(new Vector2Int(nextX, nextY));
                break; // 탐색 종료 (성공)
            }
            // B. 다음 칸이 물체인데 '함정(isDead)'임 -> 여기서 상자 소멸하고 멈춤!
            else if (nextTile.isDead) {
                hitTrap = true; // 함정에 빠짐!
                chainCoords.Add(new Vector2Int(nextX, nextY));
                break; // 탐색 종료 (성공: 상자가 죽으면서 끝남)
            }
            // C. 다음 칸도 밀 수 있는 물체 (isPush) -> 계속 탐색
            else if (nextTile.isPush) {
                curX = nextX;
                curY = nextY;
                continue;
            }
            // D. 벽(isStop) -> 못 밈 (전체 실패)
            else if (nextTile.isStop) {
                return false; 
            }
            // E. 그 외 (통과 가능) -> 빈 칸 취급
            else {
                chainCoords.Add(new Vector2Int(nextX, nextY));
                break;
            }
        }

        // 맵이 꽉 차서 빈 곳을 못 찾음
        if (!hitTrap && chainCoords.Count == chainData.Count) return false;

        // 2. 실제 데이터 이동
        // chainData[i]를 chainCoords[i+1]로 옮김
        for (int i = 0; i < chainData.Count; i++) {
            Vector2Int dest = chainCoords[i + 1];
            
            // ★ [핵심] 만약 이번이 체인의 마지막(가장 앞선 상자)이고, 함정에 빠진 상태라면?
            if (hitTrap && i == chainData.Count - 1) {
                // 상자를 목적지에 쓰지 않음 (함정은 그대로 두고 상자만 소멸)
                // maps[dest.x, dest.y, layer] 값을 덮어쓰지 않음으로써 함정 유지
                // 만약 바닥 함정 때문에 죽은거라면 그냥 빈칸(-1)이 됨
                
                // (선택) 만약 물체형 함정(가시 등)이 아니라 '구멍'이라서 
                // 함정도 같이 메워져야 한다면 여기서 덮어씌우면 됩니다.
                // 지금은 "상자가 사라져야 해"라고 하셨으므로 덮어쓰지 않습니다.
            }
            else {
                // 평소대로 이동
                maps[dest.x, dest.y, layer] = chainData[i];
            }
        }

        // 시작 지점 비우기 (-1: 빈 공간)
        maps[startX, startY, layer] = -1;

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