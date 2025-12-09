using UnityEngine;
using System; 
using System.Collections; 
using System.Collections.Generic; 

public class RubikManager : MonoBehaviour
{
    [Header("UI 매니저 연결")]
    public UIManager uiManager;

    [Header("타일 데이터 목록")]
    public TileData[] tilePalette; 

    [Header("테스트 기능")]
    public bool reloadMap = false;

    [Header("레벨 파일 (.txt) 등록")]
    public TextAsset[] levelFiles; 
    public int currentLevelIndex = 0; 

    [Header("비주얼 설정")]
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    // 데이터
    private int width;
    private int height;
    private int[,] mapData; 
    private Vector2Int playerIndex;
    
    private GameObject[,] objMap;
    private GameObject objPlayer;

    public GameObject prefabPlayer; 

    private float _lastSizeXZ;
    private float _lastHeight;
    private bool isGameEnding = false;

    void Start() => InitializeGame();

    void Update()
    {
        if (reloadMap) { reloadMap = false; InitializeGame(); }

        if (tileSizeXZ != _lastSizeXZ || tileHeight != _lastHeight)
        {
            SyncVisuals();
            _lastSizeXZ = tileSizeXZ;
            _lastHeight = tileHeight;
        }
    }

    void InitializeGame()
    {
        StopAllCoroutines();
        isGameEnding = false;
        if (uiManager != null) uiManager.HideAll();

        ClearMapVisuals();
        LoadMapFromFile(); 
        UpdateView();
        UpdatePlayerPosition();
        AutoAdjustCamera();

        _lastSizeXZ = tileSizeXZ;
        _lastHeight = tileHeight;
    }

    TileData GetTileData(int id)
    {
        foreach (var data in tilePalette)
        {
            if (data.tileID == id) return data;
        }
        return null; 
    }

    // --- 1. 플레이어 이동 (1개만 밀기) ---
    public void TryMovePlayer(int dx, int dy)
    {
        if (isGameEnding) return;

        int nextX = playerIndex.x + dx;
        int nextY = playerIndex.y + dy;

        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) return;

        TileData nextTile = GetTileData(mapData[nextX, nextY]);
        
        // [1] 앞에 상자(isPush)가 있는 경우
        if (nextTile != null && nextTile.isPush)
        {
            int pushX = nextX + dx;
            int pushY = nextY + dy;

            if (pushX < 0 || pushX >= width || pushY < 0 || pushY >= height) return;

            TileData afterBoxTile = GetTileData(mapData[pushX, pushY]);

            // 상자 뒤가 빈 공간이거나, 장애물(Stop/Push)이 없어야 밀림 (1개 제한)
            bool canPush = (afterBoxTile == null) || (!afterBoxTile.isStop && !afterBoxTile.isPush);

            if (canPush)
            {
                mapData[pushX, pushY] = mapData[nextX, nextY];
                mapData[nextX, nextY] = 0;
                UpdateView();
            }
            else
            {
                return; // 뒤가 막혀서 못 밈
            }
        }
        // [2] 벽(isStop)이면 못 감
        else if (nextTile != null && nextTile.isStop) 
        {
            return; 
        }

        playerIndex = new Vector2Int(nextX, nextY);
        UpdatePlayerPosition();
        CheckFoot();
    }

    // 좌표 래핑 헬퍼 함수
    int GetWrappedIndex(int index, int max)
    {
        return (index % max + max) % max;
    }

    // --- 2. 맵 회전 (WASD 입력) ---
public void TryPushRow(int dir)
    {
        if (isGameEnding) return;
        if (!CanPushRow(playerIndex.y, dir)) return;

        // 1. 맵을 -dir 방향으로 돌립니다.
        ShiftRow(playerIndex.y, -dir);

        // 2. 충돌 해결 [수정]
        // 맵을 -dir로 돌렸으니, 충돌 계산도 -dir 기준으로 해야 상자가 제자리(뒤쪽)를 찾습니다.
        // 기존: ResolveCollision(dir, true); -> 오류 (상자가 2칸 점프함)
        ResolveCollision(-dir, true); 
    }

    public void TryPushCol(int dir)
    {
        if (isGameEnding) return;
        if (!CanPushCol(playerIndex.x, dir)) return;

        // 1. 맵 회전
        ShiftCol(playerIndex.x, -dir);

        // 2. 충돌 해결 [수정]
        // 여기도 마찬가지로 -dir을 넣어줍니다.
        ResolveCollision(-dir, false); 
    }

    // ★ 충돌 해결 로직 (벽은 플레이어를 밀고, 상자는 플레이어가 버팀)
    void ResolveCollision(int dir, bool isRow)
    {
        int currentID = mapData[playerIndex.x, playerIndex.y];
        TileData incomingTile = GetTileData(currentID);

        if (incomingTile == null) 
        {
            CheckFoot();
            return;
        }

        // [상황 A] 벽(isStop)이 나를 덮침 -> 내가 밀려남
        if (incomingTile.isStop)
        {
            if (isRow) playerIndex.x += dir;
            else       playerIndex.y += dir;

            UpdatePlayerPosition();
            Debug.Log("벽에 밀림!");
        }
        // [상황 B] 상자(isPush)가 나를 덮침 -> 내가 버티고 상자를 밀어냄
        else if (incomingTile.isPush)
        {
            Debug.Log("상자를 버팀! 역주행 처리 시작");
            
            // 내 자리는 비워줌
            mapData[playerIndex.x, playerIndex.y] = 0;

            int boxToSave = currentID; 
            int checkX = playerIndex.x;
            int checkY = playerIndex.y;
            int loopCount = isRow ? width : height;
            
            for (int i = 0; i < loopCount; i++)
            {
                // 뒤쪽 좌표 확인
                if (isRow) checkX = GetWrappedIndex(checkX - dir, width);
                else       checkY = GetWrappedIndex(checkY - dir, height);

                int prevID = mapData[checkX, checkY];
                TileData prevTile = GetTileData(prevID);

                // 뒤가 벽이면 상자 파괴
                if (prevTile != null && prevTile.isStop)
                {
                    Debug.Log("상자 파괴됨");
                    break; 
                }
                // 뒤가 빈 곳이면 상자 배치
                else if (prevTile == null || (!prevTile.isStop && !prevTile.isPush))
                {
                    mapData[checkX, checkY] = boxToSave;
                    break; 
                }
                // 뒤가 또 상자면 계속 밀어냄
                else if (prevTile.isPush)
                {
                    mapData[checkX, checkY] = boxToSave;
                    boxToSave = prevID;
                }
            }
            UpdateView();
        }

        CheckFoot();
    }

    void CheckFoot()
    {
        int currentID = mapData[playerIndex.x, playerIndex.y];
        TileData currentTile = GetTileData(currentID);

        if (currentTile == null) return;

        if (currentTile.isDead) 
        {
            StartCoroutine(ProcessFailSequence());
        }
        else if (currentTile.isGoal) 
        {
            StartCoroutine(ProcessClearSequence());
        }
    }

    // --- 기본 기능 및 헬퍼 ---

    IEnumerator ProcessFailSequence()
    {
        isGameEnding = true; 
        if (uiManager != null) uiManager.ShowFail();
        yield return new WaitForSeconds(1.5f);
        InitializeGame(); 
    }

    IEnumerator ProcessClearSequence()
    {
        isGameEnding = true; 
        if (uiManager != null) uiManager.ShowClear();
        yield return new WaitForSeconds(1.5f);
        currentLevelIndex++; 
        InitializeGame(); 
    }

    void LoadMapFromFile()
    {
        if (levelFiles == null || levelFiles.Length == 0) return;
        if (currentLevelIndex >= levelFiles.Length) currentLevelIndex = 0; 
        
        string[] lines = levelFiles[currentLevelIndex].text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        height = lines.Length; 
        width = lines[0].Trim().Split(' ').Length; 

        mapData = new int[width, height];
        objMap = new GameObject[width, height];

        for (int y = 0; y < height; y++)
        {
            string[] numbers = lines[height - 1 - y].Trim().Split(' '); 
            for (int x = 0; x < width; x++)
            {
                if (x < numbers.Length) mapData[x, y] = int.Parse(numbers[x]);
            }
        }
        playerIndex = new Vector2Int(width / 2, height / 2);
        mapData[playerIndex.x, playerIndex.y] = 0; 
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
        UpdateView();
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
        UpdateView();
    }

    void ClearMapVisuals() { if (objMap != null) foreach (var obj in objMap) if (obj != null) Destroy(obj); }
    
    bool CanPushRow(int y, int lookDir)
    {
        int checkX = playerIndex.x + lookDir;
        if (checkX >= width) checkX = 0;
        if (checkX < 0) checkX = width - 1;

        TileData t = GetTileData(mapData[checkX, y]);
        return t != null && t.isShift; 
    }

    bool CanPushCol(int x, int lookDir)
    {
        int checkY = playerIndex.y + lookDir;
        if (checkY >= height) checkY = 0;
        if (checkY < 0) checkY = height - 1;

        TileData t = GetTileData(mapData[x, checkY]);
        return t != null && t.isShift;
    }

    void UpdateView()
    {
        ClearMapVisuals();
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int id = mapData[x, y];
                TileData data = GetTileData(id); 

                GameObject prefabToUse = null;
                if (data != null && data.prefab != null) 
                {
                    prefabToUse = data.prefab;
                }
                
                if (prefabToUse != null)
                {
                    Vector3 pos = new Vector3(x - offsetX, 0, y - offsetZ);
                    GameObject newObj = Instantiate(prefabToUse, pos, Quaternion.identity);
                    newObj.transform.parent = transform;
                    newObj.transform.localScale = new Vector3(tileSizeXZ, tileHeight, tileSizeXZ);
                    newObj.transform.position += Vector3.up * (tileHeight / 2f);
                    objMap[x, y] = newObj;
                }
            }
        }
        SyncVisuals();
    }

    void UpdatePlayerPosition()
    {
        if (objPlayer == null)
        {
            objPlayer = Instantiate(prefabPlayer);
            objPlayer.transform.parent = transform;
            var playerScript = objPlayer.GetComponent<RubikPlayer>();
            if (playerScript == null) playerScript = objPlayer.AddComponent<RubikPlayer>();
            playerScript.Init(this); 
        }
        objPlayer.transform.localScale = Vector3.one;
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;
        Vector3 pos = new Vector3(playerIndex.x - offsetX, tileHeight, playerIndex.y - offsetZ);
        objPlayer.transform.position = pos;
    }

    void SyncVisuals()
    {
        if (objMap == null) return;
        if (objPlayer != null)
        {
            Vector3 pPos = objPlayer.transform.position;
            pPos.y = tileHeight;
            objPlayer.transform.position = pPos;
        }
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                GameObject tile = objMap[x, y];
                if (tile != null)
                {
                    tile.transform.localScale = new Vector3(tileSizeXZ, tileHeight, tileSizeXZ);
                    Vector3 pos = tile.transform.position;
                    pos.y = tileHeight / 2f;
                    tile.transform.position = pos;
                }
            }
        }
    }
    
    void AutoAdjustCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.rotation = Quaternion.Euler(45f, 45f, 0); 
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(width, height) * 0.6f + 2f; 
        cam.transform.position = -cam.transform.forward * 50f;
    }
}