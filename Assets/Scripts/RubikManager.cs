using UnityEngine;
using System.Collections;
using System; 
using System.Collections.Generic;
using Newtonsoft.Json; 

// --- [변경] Stage 용어로 클래스 이름 변경 ---
[System.Serializable]
public class StageDataRoot
{
    public StageProps properties;
    public StageLayers layers;
}

[System.Serializable]
public class StageProps
{
    public string stageName; // JSON의 "stageName"과 매칭 (JSON 키도 바꿔주세요!)
    public int maxShifts;
    public int width;
    public int height;
}

[System.Serializable]
public class StageLayers
{
    public int[][] tile;   
    public int[][] ground; 
    public int[][] sky;    
}

public class RubikManager : MonoBehaviour
{
    public GridSystem gridSystem;
    public UIManager uiManager;
    public TileData[] tilePalette;
    
    // ★ [변경] 변수명 levelFiles -> stageFiles
    public TextAsset[] stageFiles; 
    
    // ★ [변경] 변수명 currentLevelIndex -> currentStageIndex
    public int currentStageIndex = 0; 
    
    public GameObject prefabPlayer;
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    [Header("게임 규칙")]
    public int maxShiftCount = 10; 
    private int _currentShifts;

    private GameObject[,] objMap;
    private GameObject objPlayer;
    private bool isGameEnding = false;
    private float _lastSizeXZ, _lastHeight;
    private int width, height;

    void Start() {
        if (gridSystem == null) gridSystem = GetComponent<GridSystem>();
        gridSystem.OnMapChanged += UpdateView;
        gridSystem.OnPlayerMoved += UpdatePlayerVis;
        gridSystem.OnTrapTriggered += () => StartCoroutine(ProcessFail());
        gridSystem.OnGoalTriggered += () => StartCoroutine(ProcessClear());
        InitializeGame();
    }

    void Update() {
        if (tileSizeXZ != _lastSizeXZ || tileHeight != _lastHeight) {
            SyncVisuals(); _lastSizeXZ = tileSizeXZ; _lastHeight = tileHeight;
        }
    }

    void InitializeGame() {
        StopAllCoroutines(); isGameEnding = false;
        
        if (uiManager != null) uiManager.HideAll();
        ClearMapVisuals();
        
        // ★ [변경] 함수명 LoadMap -> LoadStage
        LoadStage(); 
        
        // 횟수 초기화 (LoadStage 이후에 해야 JSON 값이 적용됨)
        _currentShifts = maxShiftCount;
        Debug.Log($"스테이지 시작! 남은 회전 횟수: {_currentShifts}");

        UpdateView(); UpdatePlayerVis(); AutoAdjustCamera();
    }

    public void TryMovePlayer(int dx, int dy) { 
        if(!isGameEnding) { RotatePlayer(dx, dy); gridSystem.TryMovePlayer(dx, dy); } 
    }

    public void TryPushRow(int dir) 
    { 
        if (!isGameEnding && _currentShifts > 0) 
        {
            if (gridSystem.TryPushRow(dir)) UseShiftChance();
        } 
        else if (_currentShifts <= 0) Debug.Log("회전 횟수를 모두 소진했습니다!");
    }

    public void TryPushCol(int dir) 
    { 
        if (!isGameEnding && _currentShifts > 0) 
        { 
            if (gridSystem.TryPushCol(dir)) UseShiftChance();
        }
        else if (_currentShifts <= 0) Debug.Log("회전 횟수를 모두 소진했습니다!");
    }

    void UseShiftChance()
    {
        _currentShifts--;
        Debug.Log($"회전 성공! 남은 횟수: {_currentShifts}");
    }

    // ★ [변경] 함수명 LoadMap -> LoadStage
    void LoadStage()
    {
        if (stageFiles == null || stageFiles.Length == 0) return;
        
        string jsonText = stageFiles[currentStageIndex].text;
        
        StageDataRoot data = null;
        try { data = JsonConvert.DeserializeObject<StageDataRoot>(jsonText); }
        catch (System.Exception e) { Debug.LogError("JSON 파싱 실패: " + e.Message); return; }

        if (data == null || data.properties == null) return;

        width = data.properties.width;
        height = data.properties.height;
        maxShiftCount = data.properties.maxShifts;
        
        Debug.Log($"로드된 스테이지: {data.properties.stageName}");

        int[,,] maps = new int[width, height, 3];
        objMap = new GameObject[width, height];
        
        // 기본 시작 위치는 중앙이지만, FillLayer에서 진짜 위치를 찾으면 덮어씌워짐
        Vector2Int startPos = new Vector2Int(width/2, height/2);

        // ★ FillLayer에 startPos를 ref로 넘겨서, 플레이어(0) 위치를 찾게 함
        FillLayer(maps, data.layers.tile, 0, ref startPos);   
        FillLayer(maps, data.layers.ground, 1, ref startPos); // 여기서 플레이어 위치 찾음!
        FillLayer(maps, data.layers.sky, 2, ref startPos);    

        gridSystem.Initialize(width, height, maps, tilePalette, startPos);
    }

    // ★ [수정] 플레이어 위치(0)를 찾아서 startPos를 업데이트하는 로직 추가
    void FillLayer(int[,,] targetMap, int[][] sourceLayer, int layerIndex, ref Vector2Int startPos)
    {
        if (sourceLayer == null) return;

        for (int row = 0; row < sourceLayer.Length; row++)
        {
            if (sourceLayer[row] == null) continue;
            
            for (int col = 0; col < sourceLayer[row].Length; col++)
            {
                int x = col;
                int y = height - 1 - row; 

                if (x < width && y >= 0)
                {
                    int rawId = sourceLayer[row][col]; // 원본 ID 확인
                    int id = rawId;
                    if (id == -1) id = 0; // -1은 0으로 변환

                    targetMap[x, y, layerIndex] = id;

                    // ★ [핵심] Ground 층(1)에서 원본 값이 '0'인 곳이 플레이어 시작 위치!
                    if (layerIndex == 1 && rawId == 0)
                    {
                        startPos = new Vector2Int(x, y);
                    }
                }
            }
        }
    }

    void UpdateView()
    {
        ClearMapVisuals();
        float oX = width/2f - 0.5f, oZ = height/2f - 0.5f;

        for (int x=0; x<width; x++) {
            for (int y=0; y<height; y++) {
                Vector3 pos = new Vector3(x - oX, 0, y - oZ);
                for(int l=0; l<3; l++) {
                    int pid = gridSystem.GetLayerID(x, y, l);
                    int variant = gridSystem.GetLayerVariant(x, y, l);
                    if(pid != 0) CreateObj(pid, variant, pos, l, x, y);
                }
            }
        }
    }

    void CreateObj(int id, int var, Vector3 pos, int layer, int x, int y)
    {
        TileData d = GetTileData(id);
        if (d != null) {
            VisualVariant v = d.GetVariantByWeight(var);
            if (v.prefab != null) {
                GameObject go = Instantiate(v.prefab, pos, Quaternion.Euler(v.rotation));
                
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
                foreach (Renderer r in renderers) if (v.overrideMat != null) r.material = v.overrideMat;

                go.transform.parent = transform;
                
                float hm = (layer == 0) ? 1.0f : d.heightMultiplier;
                float h = tileHeight * hm;
                float xzScale = (layer == 0) ? 1.0f : tileSizeXZ;
                
                go.transform.localScale = new Vector3(xzScale, h, xzScale);
                
                float yOff = (layer == 2) ? tileHeight * 3.0f : 0f;
                go.transform.position += Vector3.up * yOff;

                if (layer == 1) objMap[x, y] = go;
            }
        }
    }

    void SyncVisuals() {
        if(objPlayer != null) UpdatePlayerVis();
        for(int x=0; x<width; x++) {
            for(int y=0; y<height; y++) {
                if(objMap[x,y] != null) {
                    int id = gridSystem.GetLayerID(x, y, 1);
                    TileData d = GetTileData(id);
                    float h = tileHeight * (d!=null?d.heightMultiplier:1);
                    objMap[x,y].transform.localScale = new Vector3(tileSizeXZ, h, tileSizeXZ);
                }
            }
        }
    }

    void UpdatePlayerVis() {
        if (objPlayer == null) {
            objPlayer = Instantiate(prefabPlayer);
            objPlayer.transform.parent = transform;
            objPlayer.AddComponent<RubikPlayer>().Init(this);
        }
        Vector2Int i = gridSystem.PlayerIndex;
        float oX = width/2f - 0.5f, oZ = height/2f - 0.5f;
        objPlayer.transform.position = new Vector3(i.x - oX, 0f, i.y - oZ);
    }

    void RotatePlayer(int dx, int dy) {
        if(objPlayer && (dx!=0 || dy!=0)) 
            objPlayer.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dy));
    }

    TileData GetTileData(int id) { foreach(var d in tilePalette) if(d.tileID == id) return d; return null; }
    void ClearMapVisuals() { 
        if(objMap!=null) {
            foreach(var o in transform) {
                if ((UnityEngine.Object)o != objPlayer.transform && (UnityEngine.Object)o != transform) 
                    Destroy(((Transform)o).gameObject);
            }
        }
    }
    IEnumerator ProcessFail() { isGameEnding=true; uiManager?.ShowFail(); yield return new WaitForSeconds(1.5f); InitializeGame(); }
    
    // ★ [변경] 다음 스테이지로 넘어갈 때 인덱스 변수명 변경
    IEnumerator ProcessClear() { 
        isGameEnding=true; 
        uiManager?.ShowClear(); 
        yield return new WaitForSeconds(1.5f); 
        currentStageIndex++; // currentLevelIndex -> currentStageIndex
        InitializeGame(); 
    }
    
    void AutoAdjustCamera() { 
        Camera cam = Camera.main;
        if (cam == null) return;
        
        // ★ 요청하신 코드 삭제하지 않고 유지!
        cam.transform.position = -cam.transform.forward * 50f;
        
        float mapW = width * tileSizeXZ;
        float mapH = height * tileSizeXZ;
        float padding = 2.0f; 

        if (cam.orthographic) {
            float vertSize = (mapH / 2f) + padding;
            float horzSize = ((mapW / 2f) + padding) / cam.aspect;
            cam.orthographicSize = Mathf.Max(vertSize, horzSize);
        } else {
            float targetSize = Mathf.Max(mapW, mapH) + padding * 2;
            float distance = targetSize / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = Vector3.zero - (cam.transform.forward * distance);
        }
    }
    
    public void OnClickReset()
    {
        Debug.Log("스테이지를 다시 시작합니다.");
        InitializeGame(); // 기존에 만들어둔 게임 초기화 함수 재실행
    }
}
