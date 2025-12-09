using UnityEngine;
using System;

public class GridSystem : MonoBehaviour
{
    private int width;
    private int height;
    
    // ★ [핵심] 3차원 배열: [x, y, layer]
    // layer 0: Floor, 1: Object, 2: Sky
    private int[,,] maps; 
    
    private TileData[] tilePalette;
    public Vector2Int PlayerIndex { get; private set; }

    public Action OnMapChanged;
    public Action OnPlayerMoved;
    public Action OnTrapTriggered;
    public Action OnGoalTriggered;

    // 초기화: 이제 3개의 층 데이터를 모두 받습니다.
    public void Initialize(int w, int h, int[,,] loadedMaps, TileData[] palette, Vector2Int startPos)
    {
        width = w;
        height = h;
        tilePalette = palette;
        PlayerIndex = startPos;
        maps = loadedMaps; // 매니저가 파싱해준 데이터를 그대로 씀 (자동 생성 X)

        // 패킹 (모양 번호 추가)
        for (int l = 0; l < 3; l++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int id = maps[x, y, l];
                    if (id != 0) // 0(빈공간)이 아니면 모양 번호 추가
                    {
                        int variant = UnityEngine.Random.Range(0, 100);
                        maps[x, y, l] = id | (variant << 16);
                    }
                }
            }
        }
    }

    // --- 데이터 조회 ---
    public int GetLayerID(int x, int y, int layer)
    {
        if (!IsInMap(x, y)) return 0;
        return maps[x, y, layer] & 0xFFFF; // ID 반환
    }
    
    public int GetLayerVariant(int x, int y, int layer)
    {
        if (!IsInMap(x, y)) return 0;
        return (maps[x, y, layer] >> 16) & 0xFFFF; // 모양 번호 반환
    }

    private TileData GetTileDataFromPacked(int packedData)
    {
        if (packedData == 0) return null;
        int id = packedData & 0xFFFF;
        foreach (var data in tilePalette) if (data.tileID == id) return data;
        return null;
    }

    // --- [1] 플레이어 이동 (Layer 1: Object 층에서 상호작용) ---
    public void TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;
        if (!IsInMap(nextX, nextY)) return;

        // 모든 층의 벽(Stop) 확인
        for (int l = 0; l < 3; l++)
        {
            TileData t = GetTileDataFromPacked(maps[nextX, nextY, l]);
            if (t != null && t.isStop) return;
        }

        // 상자 밀기 (Layer 1에서만 발생)
        int layer = 1; 
        TileData obj = GetTileDataFromPacked(maps[nextX, nextY, layer]);

        if (obj != null && obj.isPush)
        {
            int pushX = nextX + dx;
            int pushY = nextY + dy;
            if (!IsInMap(pushX, pushY)) return;

            // 밀려날 곳 확인 (Layer 1의 장애물만 확인 - 같은 층 충돌)
            TileData destObj = GetTileDataFromPacked(maps[pushX, pushY, layer]);
            // (옵션) 바닥층(Layer 0)이 벽이면 못 밀게 할 수도 있음
            TileData destFloor = GetTileDataFromPacked(maps[pushX, pushY, 0]);

            bool canPush = (destObj == null) && (destFloor == null || !destFloor.isStop);

            if (canPush)
            {
                maps[pushX, pushY, layer] = maps[nextX, nextY, layer]; // 이동
                maps[nextX, nextY, layer] = 0; // 원래 자리는 0 (빈 공간)
                OnMapChanged?.Invoke();
            }
            else return;
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
        if (!ResolveCollision(-dir, true)) ShiftRow(PlayerIndex.y, dir);
    }

    public void TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x)) return;
        ShiftCol(PlayerIndex.x, -dir);
        if (!ResolveCollision(-dir, false)) ShiftCol(PlayerIndex.x, dir);
    }

    // --- [3] 충돌 해결 (Layer 1: Object 층이 플레이어를 밈) ---
    bool ResolveCollision(int dir, bool isRow)
    {
        // 플레이어는 Layer 1에 서 있으므로, Layer 1의 충돌을 최우선으로 처리
        if (!HandleLayerCollision(1, dir, isRow)) return false;
        
        // (필요 시 Layer 2의 충돌도 처리 가능 - 예: 날아다니는 적)
        // if (!HandleLayerCollision(2, dir, isRow)) return false; 

        CheckFoot();
        return true;
    }

    bool HandleLayerCollision(int layer, int dir, bool isRow)
    {
        int packedData = maps[PlayerIndex.x, PlayerIndex.y, layer];
        TileData tile = GetTileDataFromPacked(packedData);

        if (tile == null) return true; // 빈 공간이면 통과

        if (tile.isStop) // 벽 -> 플레이어 밀기
        {
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            if (IsInMap(newX, newY))
            {
                PlayerIndex = new Vector2Int(newX, newY);
                OnPlayerMoved?.Invoke();
                return true;
            }
            else return false;
        }
        else if (tile.isPush) // 상자 -> 역주행
        {
            maps[PlayerIndex.x, PlayerIndex.y, layer] = 0; 
            int itemToSave = packedData;
            
            int checkX = PlayerIndex.x;
            int checkY = PlayerIndex.y;
            int loopCount = isRow ? width : height;

            for (int i = 0; i < loopCount; i++)
            {
                if (isRow) checkX = GetWrappedIndex(checkX - dir, width);
                else       checkY = GetWrappedIndex(checkY - dir, height);

                TileData dest = GetTileDataFromPacked(maps[checkX, checkY, layer]);

                if (dest != null && dest.isStop) break;
                else if (dest == null)
                {
                    maps[checkX, checkY, layer] = itemToSave;
                    break;
                }
                else if (dest.isPush)
                {
                    int temp = maps[checkX, checkY, layer];
                    maps[checkX, checkY, layer] = itemToSave;
                    itemToSave = temp;
                }
            }
            OnMapChanged?.Invoke();
            return true;
        }
        return true;
    }

    void CheckFoot()
    {
        TileData f = GetTileDataFromPacked(maps[PlayerIndex.x, PlayerIndex.y, 1]); // Layer 0 확인
        if (f != null)
        {
            if (f.isDead) OnTrapTriggered?.Invoke();
            if (f.isGoal) OnGoalTriggered?.Invoke();
        }
    }

    bool CanPushRow(int y)
    {
        for (int x = 0; x < width; x++)
            for (int l = 0; l < 3; l++) // 3개 층 모두 검사
                if (IsFixed(x, y, l)) return false;
        return true;
    }

    bool CanPushCol(int x)
    {
        for (int y = 0; y < height; y++)
            for (int l = 0; l < 3; l++)
                if (IsFixed(x, y, l)) return false;
        return true;
    }

    bool IsFixed(int x, int y, int l)
    {
        TileData t = GetTileDataFromPacked(maps[x, y, l]);
        return t != null && !t.isShift;
    }

    void ShiftRow(int y, int dir)
    {
        for(int l=0; l<3; l++) ShiftArray(l, y, dir, true);
        OnMapChanged?.Invoke();
    }

    void ShiftCol(int x, int dir)
    {
        for(int l=0; l<3; l++) ShiftArray(l, x, dir, false);
        OnMapChanged?.Invoke();
    }

    void ShiftArray(int layer, int targetIndex, int dir, bool isRow)
    {
        if (isRow)
        {
            int y = targetIndex;
            if (dir == 1) {
                int last = maps[width - 1, y, layer];
                for (int x = width - 1; x > 0; x--) maps[x, y, layer] = maps[x - 1, y, layer];
                maps[0, y, layer] = last;
            } else {
                int first = maps[0, y, layer];
                for (int x = 0; x < width - 1; x++) maps[x, y, layer] = maps[x + 1, y, layer];
                maps[width - 1, y, layer] = first;
            }
        }
        else
        {
            int x = targetIndex;
            if (dir == 1) {
                int last = maps[x, height - 1, layer];
                for (int y = height - 1; y > 0; y--) maps[x, y, layer] = maps[x, y - 1, layer];
                maps[x, 0, layer] = last;
            } else {
                int first = maps[x, 0, layer];
                for (int y = 0; y < height - 1; y++) maps[x, y, layer] = maps[x, y + 1, layer];
                maps[x, height - 1, layer] = first;
            }
        }
    }

    bool IsInMap(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
    int GetWrappedIndex(int index, int max) => (index % max + max) % max;
}