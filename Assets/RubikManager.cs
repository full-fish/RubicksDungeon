using UnityEngine;
using UnityEngine.InputSystem;

public class RubikManager : MonoBehaviour
{
    [Header("테스트 기능")]
    public bool reloadMap = false;

    [Header("맵 설정")]
    public int width = 5;
    public int height = 5;

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
    private int[,] mapData;
    private Vector2Int playerIndex;
    private GameObject[,] objMap;
    private GameObject objPlayer;

    // 변경 감지용
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

        // 비주얼 실시간 반영
        if (tileSizeXZ != _lastSizeXZ || tileHeight != _lastHeight)
        {
            SyncVisuals();
            _lastSizeXZ = tileSizeXZ;
            _lastHeight = tileHeight;
        }

        if (Keyboard.current == null) return;
        HandleInput();
    }

    // ★ 게임 시작/재시작을 담당하는 함수 (순서 중요!)
    void InitializeGame()
    {
        // 1. 기존에 있던 맵 오브젝트 싹 지우기 (유령 타일 방지)
        ClearMapVisuals();

        // 2. 데이터 생성
        GenerateMap();

        // 3. 맵 화면 그리기
        UpdateView();

        // 4. 플레이어 위치 잡기 (★ 버그 1번 해결: 여기서 강제로 중앙으로 옮김)
        UpdatePlayerPosition();

        // 5. 카메라 잡기
        AutoAdjustCamera();

        _lastSizeXZ = tileSizeXZ;
        _lastHeight = tileHeight;
    }

    // ★ 화면에 있는 타일들을 깨끗이 지우는 함수
    void ClearMapVisuals()
    {
        if (objMap != null)
        {
            foreach (var obj in objMap)
            {
                if (obj != null) Destroy(obj);
            }
        }
    }

    void HandleInput()
    {
        // 1. 이동
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame) AttemptMove(1, 0);
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)  AttemptMove(-1, 0);
        if (Keyboard.current.upArrowKey.wasPressedThisFrame)    AttemptMove(0, 1);
        if (Keyboard.current.downArrowKey.wasPressedThisFrame)  AttemptMove(0, -1);

        // 2. 밀기
        if (Keyboard.current.dKey.wasPressedThisFrame) 
        {
            if (CanPushRow(playerIndex.y, 1)) ShiftRow(playerIndex.y, -1);
        }
        if (Keyboard.current.aKey.wasPressedThisFrame)
        {
            if (CanPushRow(playerIndex.y, -1)) ShiftRow(playerIndex.y, 1);
        }
        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            if (CanPushCol(playerIndex.x, 1)) ShiftCol(playerIndex.x, -1);
        }
        if (Keyboard.current.wKey.wasPressedThisFrame)
        {
            if (CanPushCol(playerIndex.x, -1)) ShiftCol(playerIndex.x, 1);
        }
    }

    void AttemptMove(int dx, int dy)
    {
        int nextX = playerIndex.x + dx;
        int nextY = playerIndex.y + dy;

        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) return;

        // 벽 체크 (1번)
        if (mapData[nextX, nextY] == 1) 
        {
            Debug.Log("벽입니다.");
            return; 
        }

        playerIndex = new Vector2Int(nextX, nextY);
        UpdatePlayerPosition();
        CheckFoot();
    }

    // ★ 발밑 확인 (수정됨)
    void CheckFoot()
    {
        int type = mapData[playerIndex.x, playerIndex.y];

        if (type == 2) 
        {
            Debug.Log("으악! 함정(2)을 밟았다!");
            // 여기서 사망 처리 가능
        }
        else if (type == 4) 
        {
            Debug.Log("★ 상자 획득! 다음 레벨로! ★");
            // 상자를 먹었으니 바로 게임 재설정 (다음 판)
            InitializeGame(); 
        }
    }

    // --- 나머지 함수들은 기존과 동일하지만, UpdateView에서 Clear 로직을 InitializeGame으로 뺐으므로 확인 필요 ---

    void GenerateMap()
    {
        mapData = new int[width, height];
        objMap = new GameObject[width, height];
        
        playerIndex = new Vector2Int(width / 2, height / 2);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    mapData[x, y] = 1; 
                else
                {
                    int rand = Random.Range(0, 100);
                    if (rand < 10) mapData[x, y] = 1;      
                    else if (rand < 20) mapData[x, y] = 2; 
                    else if (rand < 40) mapData[x, y] = 3; 
                    else mapData[x, y] = 0;                
                }
            }
        }
        mapData[playerIndex.x, playerIndex.y] = 0; 
        
        // 상자(4) 배치
        int cx = Random.Range(1, width-1);
        int cy = Random.Range(1, height-1);
        // 플레이어 위치나 벽이면 다시 뽑기 (간단한 예외처리)
        while (mapData[cx, cy] == 1 || (cx == playerIndex.x && cy == playerIndex.y))
        {
            cx = Random.Range(1, width-1);
            cy = Random.Range(1, height-1);
        }
        mapData[cx, cy] = 4;
    }

    void UpdateView()
    {
        // ★ 중요: 여기서 Destroy를 또 하면 낭비일 수 있지만, 
        // Shift(밀기) 할 때는 전체 갱신이 필요하므로 유지합니다.
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
                
                // 모양 잡기
                newObj.transform.localScale = new Vector3(tileSizeXZ, tileHeight, tileSizeXZ);
                newObj.transform.position += Vector3.up * (tileHeight / 2f);

                objMap[x, y] = newObj;
            }
        }
    }

    void UpdatePlayerPosition()
    {
        if (objPlayer == null)
        {
            objPlayer = Instantiate(prefabPlayer);
            objPlayer.transform.parent = transform;
        }
        
        // ★ 플레이어 크기도 비주얼 설정에 맞게
        objPlayer.transform.localScale = new Vector3(1f, 1f, 1f); 

        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;
        
        Vector3 pos = new Vector3(playerIndex.x - offsetX, tileHeight, playerIndex.y - offsetZ);
        objPlayer.transform.position = pos;
    }

    // --- Shift 로직 ---
    public void ShiftRow(int y, int dir)
    {
        if (dir == 1) 
        {
            int last = mapData[width - 1, y];
            for (int x = width - 1; x > 0; x--) mapData[x, y] = mapData[x - 1, y];
            mapData[0, y] = last;
        }
        else 
        {
            int first = mapData[0, y];
            for (int x = 0; x < width - 1; x++) mapData[x, y] = mapData[x + 1, y];
            mapData[width - 1, y] = first;
        }
        UpdateView();
        CheckFoot();
    }

    public void ShiftCol(int x, int dir)
    {
        if (dir == 1) 
        {
            int last = mapData[x, height - 1];
            for (int y = height - 1; y > 0; y--) mapData[x, y] = mapData[x, y - 1];
            mapData[x, 0] = last;
        }
        else 
        {
            int first = mapData[x, 0];
            for (int y = 0; y < height - 1; y++) mapData[x, y] = mapData[x, y + 1];
            mapData[x, height - 1] = first;
        }
        UpdateView();
        CheckFoot();
    }

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

    void SyncVisuals()
    {
        // (기존 SyncVisuals 코드 유지)
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