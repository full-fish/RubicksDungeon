using UnityEngine;
using System.Collections;
using System; 
using System.Collections.Generic;
using Newtonsoft.Json; 

// --- JSON 데이터 구조 ---
[System.Serializable]
public class StageDataRoot
{
    public StageProps properties;
    public StageLayers layers;
}

[System.Serializable]
public class StageProps
{
    public string stageName;
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

// --- 뒤로가기(Undo) 저장용 클래스 ---
public class GameState
{
    public int[,,] mapData;
    public Vector2Int playerPos;
    public int remainingShifts;
    public Quaternion playerRot;
public GameState(int[,,] map, Vector2Int pos, int shifts, Quaternion rot) 
    {
        this.mapData = map;
        this.playerPos = pos;
        this.remainingShifts = shifts;
        this.playerRot = rot; // 회전값 초기화
    }
}
// ------------------------

public class RubikManager : MonoBehaviour
{
    public GridSystem gridSystem;
    public UIManager uiManager;
    public TileData[] tilePalette;
    
    public TextAsset[] stageFiles; // Stage Files
    public int currentStageIndex = 0; 
    
    public GameObject prefabPlayer;
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    [Header("게임 규칙")]
    public int maxShiftCount = 10; 
    private int _currentShifts;

    // 뒤로가기 스택
    private Stack<GameState> undoStack = new Stack<GameState>();

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
        
        // 스택 초기화
        undoStack.Clear();

        if (uiManager != null) uiManager.HideAll();
        ClearMapVisuals();
        
        LoadStage(); 
        
        _currentShifts = maxShiftCount;
        Debug.Log($"스테이지 시작! 남은 회전 횟수: {_currentShifts}");

        UpdateView(); UpdatePlayerVis(); AutoAdjustCamera();
    }

    // ★ [Undo] 상태 저장 함수
void SaveState()
    {
        int[,,] currentMap = gridSystem.GetMapSnapshot();
        Vector2Int currentPos = gridSystem.PlayerIndex;
        
        // 현재 캐릭터의 회전값 가져오기 (없으면 기본값)
        Quaternion currentRot = objPlayer != null ? objPlayer.transform.rotation : Quaternion.identity;

        undoStack.Push(new GameState(currentMap, currentPos, _currentShifts, currentRot));
    }

    // ★ [Undo] 뒤로가기 버튼 기능
public void OnClickUndo()
    {
        if (isGameEnding || undoStack.Count == 0) 
        {
            Debug.Log("더 이상 뒤로 갈 수 없습니다.");
            return;
        }

        GameState lastState = undoStack.Pop();
        _currentShifts = lastState.remainingShifts;
        
        // 맵 & 위치 복구
        gridSystem.RestoreMapData(lastState.mapData, lastState.playerPos);

        // ★ 회전 복구 (캐릭터 오브젝트가 있을 때만)
        if (objPlayer != null) 
        {
            objPlayer.transform.rotation = lastState.playerRot;
        }

        UpdateView(); 
        UpdatePlayerVis(); // 위치 갱신
        Debug.Log($"실행 취소! 남은 회전 횟수: {_currentShifts}");
    }

    // ★ [Reset] 리셋 버튼 기능
    public void OnClickReset()
    {
        Debug.Log("다시 시작");
        InitializeGame();
    }

    // --- 입력 처리 (중복 없이 하나만 있어야 함!) ---
public void TryMovePlayer(int dx, int dy) { 
        if(!isGameEnding) { 
            // 1. 입력 전 상태 저장
            SaveState(); 
            
            // 2. 회전하기 전 각도 기억
            Quaternion beforeRot = objPlayer != null ? objPlayer.transform.rotation : Quaternion.identity;

            // 3. 캐릭터 회전 적용
            RotatePlayer(dx, dy); 
            
            // 4. 회전한 후 각도 기억
            Quaternion afterRot = objPlayer != null ? objPlayer.transform.rotation : Quaternion.identity;

            // 5. 이동 시도
            bool isMoved = gridSystem.TryMovePlayer(dx, dy);

            if (!isMoved) 
            {
                // ★ [핵심 로직] 이동은 실패(벽)했지만, 
                // 바라보는 방향이 바뀌었다면(before != after) -> "유의미한 행동"으로 보고 저장 유지
                // 방향도 그대로라면 -> "아무것도 안 한 것"이므로 저장 취소(Pop)
                if (beforeRot == afterRot)
                {
                    undoStack.Pop(); 
                }
            }
            // 이동 성공(isMoved == true)했다면 당연히 저장 유지
        } 
    }

    public void TryPushRow(int dir) 
    { 
        if (!isGameEnding && _currentShifts > 0) 
        {
            SaveState(); // 일단 저장
            if (gridSystem.TryPushRow(dir)) 
            {
                UseShiftChance();
            }
            else
            {
                undoStack.Pop(); // 실패하면 저장 취소
            }
        } 
        else if (_currentShifts <= 0) Debug.Log("횟수 부족");
    }

    public void TryPushCol(int dir) 
    { 
        if (!isGameEnding && _currentShifts > 0) 
        { 
            SaveState(); // 일단 저장
            if (gridSystem.TryPushCol(dir)) 
            {
                UseShiftChance();
            }
            else
            {
                undoStack.Pop(); // 실패하면 저장 취소
            }
        }
        else if (_currentShifts <= 0) Debug.Log("횟수 부족");
    }

    void UseShiftChance()
    {
        _currentShifts--;
        Debug.Log($"회전 성공! 남은 횟수: {_currentShifts}");
    }

    // --- 맵 로드 및 렌더링 ---
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
        Vector2Int startPos = new Vector2Int(width/2, height/2);
        objMap = new GameObject[width, height];

        FillLayer(maps, data.layers.tile, 0, ref startPos);   
        FillLayer(maps, data.layers.ground, 1, ref startPos); 
        FillLayer(maps, data.layers.sky, 2, ref startPos);    

        gridSystem.Initialize(width, height, maps, tilePalette, startPos);
    }

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
                    int rawId = sourceLayer[row][col];
                    int id = rawId;
                    if (id == -1) id = 0; 
                    targetMap[x, y, layerIndex] = id;

                    if (layerIndex == 1 && rawId == 0) startPos = new Vector2Int(x, y);
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
    
    IEnumerator ProcessClear() { 
        isGameEnding=true; 
        uiManager?.ShowClear(); 
        yield return new WaitForSeconds(1.5f); 
        currentStageIndex++; 
        InitializeGame(); 
    }
    
    void AutoAdjustCamera() { 
        Camera cam = Camera.main;
        if (cam == null) return;
        
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
}