using UnityEngine;
using UnityEngine.InputSystem;
using System; 
using System.Collections.Generic;

public class RubikManager : MonoBehaviour
{
    [Header("테스트 기능")]
    public bool reloadMap = false;

    [Header("레벨 파일 (.txt) 등록")]
    public TextAsset[] levelFiles; 
    public int currentLevelIndex = 0; 

    [Header("비주얼 설정")]
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    [Header("프리팹 연결")]
    public GameObject prefabFloor;       
    public GameObject prefabWall;        
    public GameObject prefabTrap;        
    public GameObject prefabFloorDetail; 
    public GameObject prefabChest;       
    public GameObject prefabPlayer;      

    // 데이터
    private int width;
    private int height;
    private int[,] mapData;
    private Vector2Int playerIndex;
    
    private GameObject[,] objMap;
    private GameObject objPlayer;

    private float _lastSizeXZ;
    private float _lastHeight;

    void Start()
    {
        InitializeGame();
    }

    void Update()
    {
        if (reloadMap)
        {
            reloadMap = false;
            InitializeGame();
            return;
        }

        if (tileSizeXZ != _lastSizeXZ || tileHeight != _lastHeight)
        {
            SyncVisuals();
            _lastSizeXZ = tileSizeXZ;
            _lastHeight = tileHeight;
        }

        if (Keyboard.current == null) return;
        HandleInput();
    }

    void InitializeGame()
    {
        ClearMapVisuals();
        LoadMapFromFile(); 
        UpdateView();
        UpdatePlayerPosition();
        AutoAdjustCamera();

        _lastSizeXZ = tileSizeXZ;
        _lastHeight = tileHeight;
    }

    void LoadMapFromFile()
    {
        if (levelFiles == null || levelFiles.Length == 0)
        {
            Debug.LogError("레벨 파일이 없습니다! Inspector에서 등록해주세요.");
            return;
        }

        if (currentLevelIndex >= levelFiles.Length) 
        {
            Debug.Log("모든 레벨 클리어! 다시 1탄으로 돌아갑니다.");
            currentLevelIndex = 0; 
        }
        
        string textData = levelFiles[currentLevelIndex].text;
        string[] lines = textData.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        height = lines.Length; 
        width = lines[0].Trim().Split(' ').Length; 

        mapData = new int[width, height];
        objMap = new GameObject[width, height];

        for (int y = 0; y < height; y++)
        {
            string[] numbers = lines[height - 1 - y].Trim().Split(' '); 

            for (int x = 0; x < width; x++)
            {
                if (x < numbers.Length)
                {
                    int tileID = int.Parse(numbers[x]);
                    mapData[x, y] = tileID;
                }
            }
        }

        playerIndex = new Vector2Int(width / 2, height / 2);
        mapData[playerIndex.x, playerIndex.y] = 0;

        Debug.Log($"=== 레벨 {currentLevelIndex + 1} 시작 ===");
    }

    // ★ 함정과 상자 처리 (질문 3, 4 해결)
    void CheckFoot()
    {
        int type = mapData[playerIndex.x, playerIndex.y];

        if (type == 2) 
        {
            Debug.Log("☠️ 으악! 함정(2)을 밟았습니다! (재시작)");
            InitializeGame(); // 현재 레벨 재시작
        }
        else if (type == 4) 
        {
            Debug.Log("🎉 상자(4) 획득! 다음 레벨로 이동합니다!");
            currentLevelIndex++; // 레벨 번호 증가
            InitializeGame();    // 다음 레벨 로드
        }
    }

    void HandleInput()
    {
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame) AttemptMove(1, 0);
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)  AttemptMove(-1, 0);
        if (Keyboard.current.upArrowKey.wasPressedThisFrame)    AttemptMove(0, 1);
        if (Keyboard.current.downArrowKey.wasPressedThisFrame)  AttemptMove(0, -1);

        if (Keyboard.current.dKey.wasPressedThisFrame) { if (CanPushRow(playerIndex.y, 1)) ShiftRow(playerIndex.y, -1); }
        if (Keyboard.current.aKey.wasPressedThisFrame) { if (CanPushRow(playerIndex.y, -1)) ShiftRow(playerIndex.y, 1); }
        if (Keyboard.current.sKey.wasPressedThisFrame) { if (CanPushCol(playerIndex.x, 1)) ShiftCol(playerIndex.x, -1); }
        if (Keyboard.current.wKey.wasPressedThisFrame) { if (CanPushCol(playerIndex.x, -1)) ShiftCol(playerIndex.x, 1); }
    }

    void AttemptMove(int dx, int dy)
    {
        int nextX = playerIndex.x + dx;
        int nextY = playerIndex.y + dy;

        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) 
        {
            Debug.Log("맵 밖으로는 못 갑니다.");
            return;
        }

        if (mapData[nextX, nextY] == 1) 
        {
            Debug.Log("벽(1)에 막혔습니다.");
            return; 
        }

        // ★ 이동 로그 추가 (질문 2)
        Debug.Log($"이동함: ({playerIndex.x}, {playerIndex.y}) -> ({nextX}, {nextY})");

        playerIndex = new Vector2Int(nextX, nextY);
        UpdatePlayerPosition();
        CheckFoot();
    }

    public void ShiftRow(int y, int dir)
    {
        // ★ 밀기 로그 추가 (질문 2)
        Debug.Log($"가로줄({y})을 {(dir == 1 ? "오른쪽" : "왼쪽")}으로 밀었습니다.");

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
        CheckFoot();
    }

    public void ShiftCol(int x, int dir)
    {
        // ★ 밀기 로그 추가 (질문 2)
        Debug.Log($"세로줄({x})을 {(dir == 1 ? "위" : "아래")}로 밀었습니다.");

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
        CheckFoot();
    }

    // --- 아래는 기존과 동일한 보조 함수들 ---
    
    void ClearMapVisuals() { if (objMap != null) foreach (var obj in objMap) if (obj != null) Destroy(obj); }
    
    bool CanPushRow(int y, int lookDir)
    {
        int checkX = playerIndex.x + lookDir;
        if (checkX >= width) checkX = 0;
        if (checkX < 0) checkX = width - 1;
        return mapData[checkX, y] != 1;
    }

    bool CanPushCol(int x, int lookDir)
    {
        int checkY = playerIndex.y + lookDir;
        if (checkY >= height) checkY = 0;
        if (checkY < 0) checkY = height - 1;
        return mapData[x, checkY] != 1;
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
                int type = mapData[x, y];
                GameObject prefab = prefabFloor;
                if (type == 1) prefab = prefabWall;
                else if (type == 2) prefab = prefabTrap;
                else if (type == 3) prefab = prefabFloorDetail;
                else if (type == 4) prefab = prefabChest;

                Vector3 pos = new Vector3(x - offsetX, 0, y - offsetZ);
                GameObject newObj = Instantiate(prefab, pos, Quaternion.identity);
                newObj.transform.parent = transform;
                newObj.transform.localScale = new Vector3(tileSizeXZ, tileHeight, tileSizeXZ);
                newObj.transform.position += Vector3.up * (tileHeight / 2f);
                objMap[x, y] = newObj;
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
        }
        objPlayer.transform.localScale = new Vector3(1f, 1f, 1f);
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
        float targetSize = Mathf.Max(width, height) * 0.6f + 2f; 
        cam.orthographicSize = targetSize;
        float distance = 50f; 
        cam.transform.position = Vector3.zero - (cam.transform.forward * distance);
    }
}