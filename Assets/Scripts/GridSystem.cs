using UnityEngine;
using System;

// 맵의 논리적인 계산(이동, 충돌)을 전담하는 클래스
public class GridSystem : MonoBehaviour
{
    // 데이터 (매니저가 넣어줌)
    private int width;
    private int height;
    private int[,] mapData;
    private TileData[] tilePalette;
    
    // 플레이어 위치
    public Vector2Int PlayerIndex { get; private set; }

    // 매니저에게 신호를 보내기 위한 이벤트
    public Action OnMapChanged;      // "맵이 변했으니 다시 그려라"
    public Action OnPlayerMoved;     // "플레이어 위치가 변했다"
    public Action OnTrapTriggered;   // "함정 밟았다"
    public Action OnGoalTriggered;   // "골인했다"

    // 초기화 함수 (매니저가 호출)
    public void Initialize(int w, int h, int[,] data, TileData[] palette, Vector2Int startPos)
    {
        width = w;
        height = h;
        mapData = data;
        tilePalette = palette;
        PlayerIndex = startPos;
    }

    // 맵 데이터 읽기 (매니저가 그림 그릴 때 사용)
    public int GetTileID(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return -1;
        return mapData[x, y];
    }

    // 내부용: ID로 데이터 찾기
    private TileData GetTileData(int id)
    {
        foreach (var data in tilePalette)
        {
            if (data.tileID == id) return data;
        }
        return null;
    }

    // --- [1] 플레이어 이동 (1개 밀기 제한) ---
    public void TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;

        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) return;

        TileData nextTile = GetTileData(mapData[nextX, nextY]);

        // 상자 밀기 체크
        if (nextTile != null && nextTile.isPush)
        {
            int pushX = nextX + dx;
            int pushY = nextY + dy;

            if (pushX < 0 || pushX >= width || pushY < 0 || pushY >= height) return;

            TileData afterBox = GetTileData(mapData[pushX, pushY]);
            
            // 상자 뒤가 비어있거나, 장애물이 없어야 함
            bool canPush = (afterBox == null) || (!afterBox.isStop && !afterBox.isPush);

            if (canPush)
            {
                mapData[pushX, pushY] = mapData[nextX, nextY];
                mapData[nextX, nextY] = 0;
                OnMapChanged?.Invoke(); // 화면 갱신 요청
            }
            else return;
        }
        else if (nextTile != null && nextTile.isStop)
        {
            return;
        }

        PlayerIndex = new Vector2Int(nextX, nextY);
        OnPlayerMoved?.Invoke(); // 플레이어 위치 갱신 요청
        CheckFoot();
    }

    // --- [2] 맵 회전 (WASD) ---
    public void TryPushRow(int dir)
    {
        if (!CanPushRow(PlayerIndex.y, dir)) return;

        ShiftRow(PlayerIndex.y, -dir);
        ResolveCollision(-dir, true); // 충돌 해결
    }

    public void TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x, dir)) return;

        ShiftCol(PlayerIndex.x, -dir);
        ResolveCollision(-dir, false);
    }

    // --- [3] 충돌 해결 (벽은 밀고, 상자는 버팀) ---
    void ResolveCollision(int dir, bool isRow)
    {
        int currentID = mapData[PlayerIndex.x, PlayerIndex.y];
        TileData incomingTile = GetTileData(currentID);

        if (incomingTile == null) 
        {
            CheckFoot();
            return;
        }

        // A. 벽이 덮침 -> 플레이어 밀려남
        if (incomingTile.isStop)
        {
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            PlayerIndex = new Vector2Int(newX, newY);
            OnPlayerMoved?.Invoke();
            Debug.Log("벽에 밀림!");
        }
        // B. 상자가 덮침 -> 플레이어가 버티고 상자 역주행
        else if (incomingTile.isPush)
        {
            Debug.Log("상자 버팀!");
            mapData[PlayerIndex.x, PlayerIndex.y] = 0; // 내 자리는 비움

            int boxToSave = currentID;
            int checkX = PlayerIndex.x;
            int checkY = PlayerIndex.y;
            int loopCount = isRow ? width : height;

            for (int i = 0; i < loopCount; i++)
            {
                if (isRow) checkX = GetWrappedIndex(checkX - dir, width);
                else       checkY = GetWrappedIndex(checkY - dir, height);

                int prevID = mapData[checkX, checkY];
                TileData prevTile = GetTileData(prevID);

                if (prevTile != null && prevTile.isStop) break; // 벽 만나면 파괴
                else if (prevTile == null || (!prevTile.isStop && !prevTile.isPush))
                {
                    mapData[checkX, checkY] = boxToSave; // 빈 곳에 배치
                    break;
                }
                else if (prevTile.isPush)
                {
                    mapData[checkX, checkY] = boxToSave; // 교체 후 계속
                    boxToSave = prevID;
                }
            }
            OnMapChanged?.Invoke();
        }
        CheckFoot();
    }

    // --- 보조 함수들 ---
    void CheckFoot()
    {
        TileData currentTile = GetTileData(mapData[PlayerIndex.x, PlayerIndex.y]);
        if (currentTile == null) return;

        if (currentTile.isDead) OnTrapTriggered?.Invoke();
        else if (currentTile.isGoal) OnGoalTriggered?.Invoke();
    }

    bool CanPushRow(int y, int lookDir)
    {
        for (int x = 0; x < width; x++)
        {
            TileData t = GetTileData(mapData[x, y]);
            if (t != null && !t.isShift) return false; // 고정축 발견
        }
        return true;
    }

    bool CanPushCol(int x, int lookDir)
    {
        for (int y = 0; y < height; y++)
        {
            TileData t = GetTileData(mapData[x, y]);
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

    int GetWrappedIndex(int index, int max) => (index % max + max) % max;
}