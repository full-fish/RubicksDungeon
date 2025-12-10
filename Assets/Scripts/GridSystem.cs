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
    private const int PLAYER_LAYER = 1;
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

        if (tile == null) return true;

        if (tile.isStop) // A. 벽이 덮침 -> 플레이어 밀기 (기존 로직 유지)
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
        else if (tile.isPush) // B. 상자가 덮침 -> 벽이 미는지 확인 (Chain Push 로직)
        {
            // 1. 역추적: 상자 줄의 끝에 무엇이 있는지 확인
            bool isPushedByWall = false;
            int scanX = PlayerIndex.x;
            int scanY = PlayerIndex.y;
            int loopCount = isRow ? width : height;

            for (int i = 0; i < loopCount; i++)
            {
                // Layer가 이동해 온 반대 방향(-dir)으로 검사
                if (isRow) scanX = GetWrappedIndex(scanX - dir, width);
                else       scanY = GetWrappedIndex(scanY - dir, height);

                TileData backTile = GetTileDataFromPacked(maps[scanX, scanY, layer]);

                if (backTile != null && backTile.isStop)
                {
                    isPushedByWall = true; // ★ 뒤에 벽(Wall) 발견! -> 플레이어 밀어야 함
                    break;
                }
                else if (backTile == null || (!backTile.isStop && !backTile.isPush))
                {
                    isPushedByWall = false; // 뒤에 빈 공간이나 장애물 아닌 것 발견! -> 상자 역주행
                    break;
                }
                // else: 또 다른 상자이므로 계속 뒤로 스캔 (Box Chain)
            }
            
            // 2. 판정 결과에 따른 행동
            if (isPushedByWall)
            {
                // Wall -> Box -> Player 상황: 플레이어가 밀려나야 함.
                int newX = PlayerIndex.x + (isRow ? dir : 0);
                int newY = PlayerIndex.y + (isRow ? 0 : dir);
                
                if (IsInMap(newX, newY))
                {
                    PlayerIndex = new Vector2Int(newX, newY);
                    OnPlayerMoved?.Invoke();
                    return true; // 성공! (모두 밀림)
                }
                else 
                {
                    return false; // 맵 밖이라 밀릴 수 없음 -> 전체 맵 회전 취소
                }
            }
            else 
            {
                // Box drifts in 상황: 플레이어가 버티고 상자는 뒤로 밀려남 (Domino Logic 유지)
                
                maps[PlayerIndex.x, PlayerIndex.y, layer] = 0; 
                int itemToSave = packedData;
                
                // 상자를 역주행 시켜 빈 공간으로 보냄
                scanX = PlayerIndex.x;
                scanY = PlayerIndex.y;

                for (int i = 0; i < loopCount; i++)
                {
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

    void CheckFoot()
    {
       int myLayerData = maps[PlayerIndex.x, PlayerIndex.y, PLAYER_LAYER];

        CheckLayerEvent(myLayerData);
    }
    void CheckLayerEvent(int packedData)
    {
        TileData t = GetTileDataFromPacked(packedData);
        if (t != null)
        {
            if (t.isDead) OnTrapTriggered?.Invoke();
            if (t.isGoal) OnGoalTriggered?.Invoke();
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