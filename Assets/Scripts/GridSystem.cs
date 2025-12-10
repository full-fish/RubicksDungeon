using UnityEngine;
using System;

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
    
    private const int PLAYER_LAYER = 1; // 캐릭터는 1층

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

    // --- [1] 플레이어 이동 ---
    // --- [1] 플레이어 이동 (void -> bool 변경) ---
    public bool TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;
        
        // 1. 맵 밖으로 나가면 실패
        if (!IsInMap(nextX, nextY)) return false;

        // 2. 모든 층의 벽(Stop) 확인 -> 막히면 실패
        for (int l = 0; l < 3; l++) {
            TileData t = GetTileDataFromPacked(maps[nextX, nextY, l]);
            if (t != null && t.isStop) return false;
        }

        // 3. 상자 밀기 (Layer 1에서만 발생)
        int layer = 1; 
        TileData obj = GetTileDataFromPacked(maps[nextX, nextY, layer]);

        if (obj != null && obj.isPush) {
            int pushX = nextX + dx;
            int pushY = nextY + dy;
            
            // 미는 곳이 맵 밖이면 실패
            if (!IsInMap(pushX, pushY)) return false;

            TileData destObj = GetTileDataFromPacked(maps[pushX, pushY, layer]);
            TileData destFloor = GetTileDataFromPacked(maps[pushX, pushY, 0]);

            // 밀 공간이 비어있고 & 바닥이 막힌 곳이 아니어야 함
            bool canPush = (destObj == null) && (destFloor == null || !destFloor.isStop);

            if (canPush) {
                maps[pushX, pushY, layer] = maps[nextX, nextY, layer]; 
                maps[nextX, nextY, layer] = 0; 
                OnMapChanged?.Invoke();
            } else return false; // 상자가 막혀서 못 밀면 실패
        }

        // 4. 이동 성공
        PlayerIndex = new Vector2Int(nextX, nextY);
        OnPlayerMoved?.Invoke();
        CheckFoot();
        return true; // 성공 반환
    }

    // --- [2] 맵 회전 (수정됨: bool 반환) ---
    public bool TryPushRow(int dir)
    {
        if (!CanPushRow(PlayerIndex.y)) return false; // 회전 불가
        
        ShiftRow(PlayerIndex.y, -dir);
        
        if (!ResolveCollision(-dir, true))
        {
            ShiftRow(PlayerIndex.y, dir); // 충돌 해결 실패 시 원상복구
            return false; // 회전 실패
        }
        return true; // 회전 성공
    }

    public bool TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x)) return false; // 회전 불가
        
        ShiftCol(PlayerIndex.x, -dir);
        
        if (!ResolveCollision(-dir, false))
        {
            ShiftCol(PlayerIndex.x, dir);
            return false; // 회전 실패
        }
        return true; // 회전 성공
    }

    // --- [3] 충돌 해결 ---
    bool ResolveCollision(int dir, bool isRow) {
        if (!HandleLayerCollision(1, dir, isRow)) return false;
        CheckFoot();
        return true;
    }

    bool HandleLayerCollision(int layer, int dir, bool isRow) {
        int packedData = maps[PlayerIndex.x, PlayerIndex.y, layer];
        TileData tile = GetTileDataFromPacked(packedData);

        if (tile == null) return true;

        if (tile.isStop) {
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            if (IsInMap(newX, newY)) {
                PlayerIndex = new Vector2Int(newX, newY);
                OnPlayerMoved?.Invoke();
                return true;
            } else return false;
        } else if (tile.isPush) {
            bool isPushedByWall = false;
            int scanX = PlayerIndex.x;
            int scanY = PlayerIndex.y;
            int loopCount = isRow ? width : height;

            for (int i = 0; i < loopCount; i++) {
                if (isRow) scanX = GetWrappedIndex(scanX - dir, width);
                else       scanY = GetWrappedIndex(scanY - dir, height);

                TileData backTile = GetTileDataFromPacked(maps[scanX, scanY, layer]);
                if (backTile != null && backTile.isStop) { isPushedByWall = true; break; }
                else if (backTile == null || (!backTile.isStop && !backTile.isPush)) { isPushedByWall = false; break; }
            }
            
            if (isPushedByWall) {
                int newX = PlayerIndex.x + (isRow ? dir : 0);
                int newY = PlayerIndex.y + (isRow ? 0 : dir);
                if (IsInMap(newX, newY)) {
                    PlayerIndex = new Vector2Int(newX, newY);
                    OnPlayerMoved?.Invoke();
                    return true;
                } else return false;
            } else {
                maps[PlayerIndex.x, PlayerIndex.y, layer] = 0; 
                int itemToSave = packedData;
                scanX = PlayerIndex.x; scanY = PlayerIndex.y;

                for (int i = 0; i < loopCount; i++) {
                    if (isRow) scanX = GetWrappedIndex(scanX - dir, width);
                    else       scanY = GetWrappedIndex(scanY - dir, height);
                    TileData dest = GetTileDataFromPacked(maps[scanX, scanY, layer]);
                    if (dest != null && dest.isStop) break;
                    else if (dest == null) { maps[scanX, scanY, layer] = itemToSave; break; } 
                    else if (dest.isPush) { int tmp = maps[scanX, scanY, layer]; maps[scanX, scanY, layer] = itemToSave; itemToSave = tmp; }
                }
                OnMapChanged?.Invoke();
                return true;
            }
        }
        return true;
    }

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

    public int[,,] GetMapSnapshot()
    {
        return (int[,,])maps.Clone(); // 배열 복제
    }

    // ★ [추가] 저장된 맵 데이터로 상태 복구
    public void RestoreMapData(int[,,] savedMap, Vector2Int savedPlayerIndex)
    {
        maps = savedMap;
        PlayerIndex = savedPlayerIndex;
        // 맵이 바뀌었으니 이벤트 호출 (시각적 갱신을 위해)
        OnMapChanged?.Invoke(); 
        OnPlayerMoved?.Invoke();
    }
    }

