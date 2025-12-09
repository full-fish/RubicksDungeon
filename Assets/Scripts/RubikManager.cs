using UnityEngine;
using System.Collections;
using System; 

public class RubikManager : MonoBehaviour
{
    [Header("핵심 연결")]
    public GridSystem gridSystem; 
    public UIManager uiManager;

    [Header("데이터")]
    public TileData[] tilePalette;
    public TextAsset[] levelFiles; 
    public int currentLevelIndex = 0; 

    [Header("비주얼")]
    public GameObject prefabPlayer;
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    private GameObject[,] objMap;
    private GameObject objPlayer;
    private bool isGameEnding = false;

    // 실시간 갱신 감지용
    private float _lastSizeXZ;
    private float _lastHeight;
    private int width;
    private int height;

    void Start()
    {
        if (gridSystem == null) gridSystem = GetComponent<GridSystem>();
        
        // GridSystem 이벤트 연결
        gridSystem.OnMapChanged += UpdateView;
        gridSystem.OnPlayerMoved += UpdatePlayerVis;
        gridSystem.OnTrapTriggered += () => StartCoroutine(ProcessFail());
        gridSystem.OnGoalTriggered += () => StartCoroutine(ProcessClear());

        InitializeGame();
    }

    // 실시간 비주얼 갱신 (Play 모드 중 인스펙터 조절 반영)
    void Update()
    {
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
        LoadMapAndInitGrid(); 

        UpdateView();
        UpdatePlayerVis();
        AutoAdjustCamera();

        _lastSizeXZ = tileSizeXZ;
        _lastHeight = tileHeight;
    }

    // 입력 전달
    public void TryMovePlayer(int dx, int dy) 
    { 
        if(!isGameEnding) 
        {
            RotatePlayer(dx, dy); // ★ 먼저 몸을 돌림
            gridSystem.TryMovePlayer(dx, dy); // 그 다음 이동 로직 실행
        }
    }
    public void TryPushRow(int dir)           { if(!isGameEnding) gridSystem.TryPushRow(dir); }
    public void TryPushCol(int dir)           { if(!isGameEnding) gridSystem.TryPushCol(dir); }

    void LoadMapAndInitGrid()
    {
        if (levelFiles == null || levelFiles.Length == 0) return;
        if (currentLevelIndex >= levelFiles.Length) currentLevelIndex = 0;

        string[] lines = levelFiles[currentLevelIndex].text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        height = lines.Length; 
        width = lines[0].Trim().Split(' ').Length; 

        int[,] mapData = new int[width, height];
        objMap = new GameObject[width, height];

        // 기본값은 중앙이지만, 0번을 찾으면 바뀔 예정
        Vector2Int startPos = new Vector2Int(width / 2, height / 2); 

        for (int y = 0; y < height; y++)
        {
            string[] numbers = lines[height - 1 - y].Trim().Split(' '); 
            for (int x = 0; x < width; x++)
            {
                if (x < numbers.Length) 
                {
                    int parsedID = int.Parse(numbers[x]);

                    // ★ [핵심 수정] 맵 파일에서 0(플레이어)을 발견하면?
                    if (parsedID == 0)
                    {
                        startPos = new Vector2Int(x, y); // 시작 위치로 등록
                        mapData[x, y] = 100; // 그 자리는 '기본 바닥(100)'으로 채움
                    }
                    else
                    {
                        mapData[x, y] = parsedID; // 나머지는 그대로 저장
                    }
                }
            }
        }
        
        // [삭제됨] mapData[startPos.x, startPos.y] = 0; <- 이 강제 초기화 코드는 삭제했습니다.

        gridSystem.Initialize(width, height, mapData, tilePalette, startPos);
    }

   void UpdateView()
    {
        ClearMapVisuals();
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;

        TileData baseFloorData = GetTileData(100);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int id = gridSystem.GetTileID(x, y);
                int variantNum = gridSystem.GetTileVariant(x, y);
                TileData data = GetTileData(id);

                if (data != null)
                {
                    Vector3 pos = new Vector3(x - offsetX, 0, y - offsetZ);

                    // ★ [계산] 이 타일의 최종 높이 = 기본 높이 * 개별 배율
                    float myHeight = tileHeight * data.heightMultiplier;

                    // 1. 밑바닥 깔기 (바닥은 항상 기본 높이 tileHeight 사용)
                    if (data.hasFloorUnder && baseFloorData != null)
                    {
                        VisualVariant floorVar = baseFloorData.GetVariantByWeight(variantNum);
                        if (floorVar.prefab != null)
                        {
                            // 메인 오브젝트 생성
                            VisualVariant mainVar = data.GetVariantByWeight(variantNum);
                            GameObject mainObj = Instantiate(mainVar.prefab, pos, Quaternion.identity);
                            ApplyMaterial(mainObj, mainVar.overrideMat);

                            mainObj.transform.parent = transform;
                            // ★ 높이 적용 (myHeight)
                            mainObj.transform.localScale = new Vector3(tileSizeXZ, myHeight, tileSizeXZ);
                            mainObj.transform.position += Vector3.up * (myHeight / 2f);

                            // 바닥 생성
                            GameObject floorObj = Instantiate(floorVar.prefab, pos, Quaternion.identity);
                            ApplyMaterial(floorObj, floorVar.overrideMat);

                            floorObj.transform.SetParent(mainObj.transform);
                            
                            // 바닥 크기 보정 (부모가 커졌으면 자식은 작아져야 원래 크기 유지)
                            // 부모 높이가 2배면, 자식 Y스케일은 0.5여야 함
                            float inverseScale = 1.0f / data.heightMultiplier;
                            floorObj.transform.localScale = new Vector3(1, inverseScale, 1);
                            
                            // 바닥 위치를 발밑(y=0)으로 내리기
                            // 부모의 Pivot이 중앙이라 자식 위치 잡기가 까다로울 수 있음.
                            // 가장 쉬운 건 바닥은 그냥 (0, -0.5, 0) 근처로 내리는 것인데, 
                            // 일단 scale만 맞춰도 묻혀서 안 보일 일은 줄어듭니다.
                            floorObj.transform.localPosition = new Vector3(0, -0.5f + (0.5f * inverseScale), 0); 

                            objMap[x, y] = mainObj;
                            continue;
                        }
                    }

                    // 2. 일반 생성
                    VisualVariant v = data.GetVariantByWeight(variantNum);
                    if (v.prefab != null)
                    {
                        GameObject newObj = Instantiate(v.prefab, pos, Quaternion.identity);
                        ApplyMaterial(newObj, v.overrideMat);

                        newObj.transform.parent = transform;
                        // ★ 높이 적용 (myHeight)
                        newObj.transform.localScale = new Vector3(tileSizeXZ, myHeight, tileSizeXZ);
                        newObj.transform.position += Vector3.up * (myHeight / 2f);
                        objMap[x, y] = newObj;
                    }
                }
            }
        }
    }

    void ApplyMaterial(GameObject obj, Material mat)
    {
        if (mat != null)
        {
            Renderer r = obj.GetComponentInChildren<Renderer>();
            if (r != null) r.material = mat;
        }
    }

    void UpdatePlayerVis()
    {
        if (objPlayer == null)
        {
            objPlayer = Instantiate(prefabPlayer);
            objPlayer.transform.parent = transform;
            var playerScript = objPlayer.GetComponent<RubikPlayer>();
            if (playerScript == null) playerScript = objPlayer.AddComponent<RubikPlayer>();
            playerScript.Init(this);
        }
        
        Vector2Int idx = gridSystem.PlayerIndex;
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;
        Vector3 pos = new Vector3(idx.x - offsetX, tileHeight, idx.y - offsetZ);
        objPlayer.transform.position = pos;
    }

    void SyncVisuals()
    {
        if (objMap == null || gridSystem == null) return;

        // ... (플레이어 갱신 코드는 그대로) ...

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                GameObject tile = objMap[x, y];
                if (tile != null)
                {
                    // 데이터 가져오기
                    int id = gridSystem.GetTileID(x, y);
                    TileData data = GetTileData(id);
                    
                    // 기본값 1.0 (데이터가 없거나 못 찾으면)
                    float multiplier = (data != null) ? data.heightMultiplier : 1.0f;
                    
                    // ★ 개별 높이 계산
                    float myHeight = tileHeight * multiplier;

                    tile.transform.localScale = new Vector3(tileSizeXZ, myHeight, tileSizeXZ);
                    
                    Vector3 pos = tile.transform.position;
                    pos.y = myHeight / 2f;
                    tile.transform.position = pos;
                }
            }
        }
    }
    TileData GetTileData(int id)
    {
        foreach (var data in tilePalette) if (data.tileID == id) return data;
        return null;
    }

    IEnumerator ProcessFail()
    {
        isGameEnding = true;
        if (uiManager != null) uiManager.ShowFail();
        yield return new WaitForSeconds(1.5f);
        InitializeGame();
    }

    IEnumerator ProcessClear()
    {
        isGameEnding = true;
        if (uiManager != null) uiManager.ShowClear();
        yield return new WaitForSeconds(1.5f);
        currentLevelIndex++;
        InitializeGame();
    }

    void ClearMapVisuals() { if (objMap != null) foreach (var obj in objMap) if (obj != null) Destroy(obj); }
    
    void AutoAdjustCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.rotation = Quaternion.Euler(45f, 45f, 0); 
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(width, height) * 0.6f + 2f; 
        cam.transform.position = -cam.transform.forward * 50f;
    }
    // 플레이어 회전시키는 함수
    void RotatePlayer(int dx, int dy)
    {
        if (objPlayer == null) return;

        // 입력받은 dx, dy를 3D 방향(x, 0, z)으로 변환
        Vector3 direction = new Vector3(dx, 0, dy);

        // 방향이 0이 아닐 때만 회전 (가만히 있을 때 0,0,0이 되면 회전값이 깨짐)
        if (direction != Vector3.zero)
        {
            // "이 방향을 바라보라"는 회전값(Quaternion)을 만듭니다.
            objPlayer.transform.rotation = Quaternion.LookRotation(direction);
        }
    }
}