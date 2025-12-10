using UnityEngine;
using System.Collections;
using System; 
using System.Collections.Generic;

public class RubikManager : MonoBehaviour
{
    public GridSystem gridSystem;
    public UIManager uiManager;
    public TileData[] tilePalette;
    public TextAsset[] levelFiles; 
    public int currentLevelIndex = 0; 
    
    public GameObject prefabPlayer;
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    // ★ [추가] 횟수 제한 설정
    [Header("게임 규칙")]
    [Tooltip("이 스테이지에서 맵을 돌릴 수 있는 최대 횟수")]
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
        
        // ★ [추가] 횟수 초기화
        _currentShifts = maxShiftCount;
        Debug.Log($"게임 시작! 남은 회전 횟수: {_currentShifts}");

        if (uiManager != null) uiManager.HideAll();
        ClearMapVisuals();
        LoadMap(); 
        UpdateView(); UpdatePlayerVis(); AutoAdjustCamera();
    }

    // --- [수정] 횟수 제한 적용된 입력 처리 ---
    public void TryMovePlayer(int dx, int dy) { 
        if(!isGameEnding) { RotatePlayer(dx, dy); gridSystem.TryMovePlayer(dx, dy); } 
    }

    public void TryPushRow(int dir) 
    { 
        if (!isGameEnding && _currentShifts > 0) 
        {
            // GridSystem이 성공(true)을 반환하면 횟수 차감
            if (gridSystem.TryPushRow(dir)) 
            {
                UseShiftChance();
            }
        } 
        else if (_currentShifts <= 0)
        {
            Debug.Log("회전 횟수를 모두 소진했습니다!");
        }
    }

    public void TryPushCol(int dir) 
    { 
        if (!isGameEnding && _currentShifts > 0) 
        { 
            if (gridSystem.TryPushCol(dir)) 
            {
                UseShiftChance();
            }
        }
        else if (_currentShifts <= 0)
        {
            Debug.Log("회전 횟수를 모두 소진했습니다!");
        }
    }

    // 횟수 사용 처리 함수
    void UseShiftChance()
    {
        _currentShifts--;
        Debug.Log($"회전 성공! 남은 횟수: {_currentShifts}");
        // 나중에 UI 매니저에 연동하면 됩니다.
        // if(uiManager != null) uiManager.UpdateShiftCount(_currentShifts);
    }

    // ---------------------------------------------------------

    void LoadMap()
    {
        if (levelFiles == null || levelFiles.Length == 0) return;
        string text = levelFiles[currentLevelIndex].text.Replace("\r", "");
        
        string[] layerBlocks = text.Split(new string[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
        string[] firstLayerLines = layerBlocks[0].Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        height = firstLayerLines.Length;
        width = firstLayerLines[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        int[,,] maps = new int[width, height, 3];
        Vector2Int startPos = new Vector2Int(width/2, height/2);
        objMap = new GameObject[width, height];

        for (int l = 0; l < 3; l++) {
            if (l >= layerBlocks.Length) break;
            string[] lines = layerBlocks[l].Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int y = 0; y < height; y++) {
                string[] nums = lines[height - 1 - y].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int x = 0; x < width; x++) {
                    if(x < nums.Length) {
                        int id = int.Parse(nums[x]);
                        if (id == -1) id = 0;
                        if (id == 0 && nums[x] == "0") {
                            if (l == 1) startPos = new Vector2Int(x, y);
                            id = 0;
                        }
                        maps[x, y, l] = id;
                    }
                }
            }
        }
        gridSystem.Initialize(width, height, maps, tilePalette, startPos);
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
                
                // 그림자 설정이 필요 없다면 아래 렌더러 부분은 지워도 됩니다.
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
    IEnumerator ProcessClear() { isGameEnding=true; uiManager?.ShowClear(); yield return new WaitForSeconds(1.5f); currentLevelIndex++; InitializeGame(); }
    
    void AutoAdjustCamera() { 
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = -cam.transform.forward * 50f;
        
        // 맵 크기 비례 자동 조절
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