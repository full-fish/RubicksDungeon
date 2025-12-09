using UnityEngine;
using UnityEngine.InputSystem;

public class RubikManager : MonoBehaviour
{
    [Header("게임 상태")]
    public int currentLevel = 1; // 현재 레벨 표시용

    [Header("맵 설정")]
    public int width = 5;
    public int height = 5;

    [Header("비주얼 설정")]
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    [Header("프리팹")]
    public GameObject prefabFloor;       // 0: 바닥
    public GameObject prefabWall;        // 1: 벽
    public GameObject prefabTrap;        // 2: 함정
    public GameObject prefabChest;       // ★ 4: 상자 (목표물)
    public GameObject prefabPlayer;      

    // 데이터
    private int[,] mapData;
    private Vector2Int playerIndex;      // 플레이어 위치 (항상 중앙)

    private GameObject[,] objMap;
    private GameObject objPlayer;

    void Start()
    {
        StartLevel();
    }

    void StartLevel()
    {
        Debug.Log($"=== LEVEL {currentLevel} START ===");
        GenerateMap();
        UpdateView();
        UpdatePlayerPosition();
        AutoAdjustCamera();
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        // 플레이어 이동
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame) MovePlayer(1, 0);
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)  MovePlayer(-1, 0);
        if (Keyboard.current.upArrowKey.wasPressedThisFrame)    MovePlayer(0, 1);
        if (Keyboard.current.downArrowKey.wasPressedThisFrame)  MovePlayer(0, -1);

        // 땅 밀기
        if (Keyboard.current.dKey.wasPressedThisFrame) 
        {
            if (CheckIncomingWall_Row(playerIndex.y, 1)) ShiftRow(playerIndex.y, -1);
        }
        if (Keyboard.current.aKey.wasPressedThisFrame)
        {
            if (CheckIncomingWall_Row(playerIndex.y, -1)) ShiftRow(playerIndex.y, 1);
        }
        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            if (CheckIncomingWall_Col(playerIndex.x, 1)) ShiftCol(playerIndex.x, -1);
        }
        if (Keyboard.current.wKey.wasPressedThisFrame)
        {
            if (CheckIncomingWall_Col(playerIndex.x, -1)) ShiftCol(playerIndex.x, 1);
        }
    }

    // ★ 발밑 확인 로직 (핵심)
    void CheckFoot()
    {
        int type = mapData[playerIndex.x, playerIndex.y];

        if (type == 1) 
        {
            Debug.Log("벽에 꼈다! (게임 오버)");
            // TODO: 게임 오버 씬으로 이동하거나 재시작
        }
        else if (type == 2) 
        {
            Debug.Log("함정을 밟았다! (사망)");
            // TODO: 사망 처리
        }
        else if (type == 4) // ★ 4번은 상자!
        {
            Debug.Log($"★ 상자 획득! 레벨 {currentLevel} 클리어! ★");
            currentLevel++; // 레벨 올리고
            StartLevel();   // 다음 판 시작 (맵 재생성)
        }
    }

    void GenerateMap()
    {
        mapData = new int[width, height];
        objMap = new GameObject[width, height];
        playerIndex = new Vector2Int(width / 2, height / 2);

        // 1. 기본 맵 생성
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    mapData[x, y] = 1; // 테두리 벽
                else
                {
                    int rand = Random.Range(0, 100);
                    if (rand < 10) mapData[x, y] = 1;      // 10% 벽
                    else if (rand < 20) mapData[x, y] = 2; // 10% 함정
                    else mapData[x, y] = 0;                // 나머지 바닥
                }
            }
        }
        
        // 내 위치는 안전하게
        mapData[playerIndex.x, playerIndex.y] = 0;

        // 2. 상자(4번) 배치 (무조건 하나는 있어야 함)
        PlaceChest();
    }

    void PlaceChest()
    {
        int attempts = 0;
        while (attempts < 100) // 혹시라도 무한루프 돌지 않게 안전장치
        {
            int rx = Random.Range(1, width - 1);
            int ry = Random.Range(1, height - 1);

            // 빈 바닥(0)이고, 플레이어 위치가 아니면 상자 배치
            if (mapData[rx, ry] == 0 && (rx != playerIndex.x || ry != playerIndex.y))
            {
                mapData[rx, ry] = 4; // 4번: 상자
                break; // 성공했으니 탈출
            }
            attempts++;
        }
    }

    void UpdateView()
    {
        foreach (var obj in objMap) if (obj != null) Destroy(obj);
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int type = mapData[x, y];
                
                // ★ 타입별 프리팹 연결
                GameObject prefab = prefabFloor;
                if (type == 1) prefab = prefabWall;
                else if (type == 2) prefab = prefabTrap;
                else if (type == 4) prefab = prefabChest; // 상자

                Vector3 pos = new Vector3(x - offsetX, 0, y - offsetZ);
                GameObject newObj = Instantiate(prefab, pos, Quaternion.identity);
                newObj.transform.parent = transform;
                newObj.transform.localScale = new Vector3(tileSizeXZ, tileHeight, tileSizeXZ);
                newObj.transform.position += Vector3.up * (tileHeight / 2f);
                
                // (선택) 상자나 함정은 색깔로 구분 (모델이 없으면)
                if (type == 4) newObj.GetComponent<Renderer>().material.color = Color.yellow; // 노란색
                if (type == 2) newObj.GetComponent<Renderer>().material.color = Color.red;    // 빨간색

                objMap[x, y] = newObj;
            }
        }
    }

    // --- 아래 이동/밀기 로직은 기존과 100% 동일 ---
    void MovePlayer(int dx, int dy)
    {
        int nextX = playerIndex.x + dx;
        int nextY = playerIndex.y + dy;
        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) return;
        if (mapData[nextX, nextY] == 1) { Debug.Log("벽!"); return; }

        playerIndex = new Vector2Int(nextX, nextY);
        UpdatePlayerPosition();
        CheckFoot(); // 이동 후 밟았는지 확인
    }

    void HandleShift() { /* (기존과 동일하게 d, a, s, w 키 처리) */ }
    
    // ShiftRow, ShiftCol, CheckIncomingWall 함수들...
    // (기존 코드 그대로 복사해서 쓰시면 됩니다. 
    //  ShiftRow/Col 마지막에 CheckFoot() 호출하는 것 잊지 마세요!)
    
    // 편의를 위해 Shift 함수에 CheckFoot 추가한 버전:
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
        CheckFoot(); // ★ 밀고 나서 내 발밑에 상자가 왔는지 확인!
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
        CheckFoot(); // ★ 확인!
    }

    // CheckIncomingWall_Row, Col 도 기존과 동일
    bool CheckIncomingWall_Row(int y, int lookDir)
    {
        int checkX = playerIndex.x + lookDir;
        if (checkX >= width) checkX = 0;
        if (checkX < 0) checkX = width - 1;
        return mapData[checkX, y] != 1;
    }
    bool CheckIncomingWall_Col(int x, int lookDir)
    {
        int checkY = playerIndex.y + lookDir;
        if (checkY >= height) checkY = 0;
        if (checkY < 0) checkY = height - 1;
        return mapData[x, checkY] != 1;
    }

    void UpdatePlayerPosition()
    {
        if (objPlayer == null)
        {
            objPlayer = Instantiate(prefabPlayer);
            objPlayer.transform.parent = transform;
            objPlayer.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f); // 캐릭터 크기
        }
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;
        Vector3 pos = new Vector3(playerIndex.x - offsetX, tileHeight, playerIndex.y - offsetZ);
        objPlayer.transform.position = pos;
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