using UnityEngine;
using System.Collections;
using System; 
using System.Collections.Generic; // List 사용

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

    private GameObject[,] objMap; // 1층 오브젝트만 대표 저장
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
        LoadMap(); 
        UpdateView(); UpdatePlayerVis(); AutoAdjustCamera();
    }

    public void TryMovePlayer(int dx, int dy) { 
        if(!isGameEnding) { RotatePlayer(dx, dy); gridSystem.TryMovePlayer(dx, dy); } 
    }
    public void TryPushRow(int dir) { if(!isGameEnding) gridSystem.TryPushRow(dir); }
    public void TryPushCol(int dir) { if(!isGameEnding) gridSystem.TryPushCol(dir); }

    // ★ [핵심] 3개 층 맵 파일 파싱 함수
    void LoadMap()
    {
        if (levelFiles == null || levelFiles.Length == 0) return;
        string text = levelFiles[currentLevelIndex].text.Replace("\r", "");
        
        // 1. "---" 구분자로 층 분리
        // (Layer 0, Layer 1, Layer 2 순서)
        string[] layerBlocks = text.Split(new string[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
        
        // 2. 맵 크기 측정 (첫 번째 블록 기준)
        string[] firstLayerLines = layerBlocks[0].Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        height = firstLayerLines.Length;
        width = firstLayerLines[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // 3. 3차원 배열 생성
        int[,,] maps = new int[width, height, 3];
        Vector2Int startPos = new Vector2Int(width/2, height/2);
        objMap = new GameObject[width, height];

        // 4. 각 층 데이터 채우기
        for (int l = 0; l < 3; l++)
        {
            if (l >= layerBlocks.Length) break; // 맵 파일에 층이 부족하면 중단

            string[] lines = layerBlocks[l].Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // 텍스트 파일은 위에서 아래로 읽지만, 유니티 좌표(y)는 아래에서 위로 증가하므로 뒤집어서 읽음
            for (int y = 0; y < height; y++)
            {
                // lines[0]이 맨 위 줄(y = height-1)이어야 함
                string[] nums = lines[height - 1 - y].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                for (int x = 0; x < width; x++)
                {
                    if (x < nums.Length)
                    {
                        int id = int.Parse(nums[x]);
                        
                        // -1은 빈 공간(0)으로 처리
                        if (id == -1) id = 0;

                        // 0번(플레이어) 발견 시 시작 위치 저장하고 빈 공간으로 만듦
                        if (id == 0 && nums[x] == "0") // 텍스트가 명시적으로 "0"일 때
                        {
                            if (l == 1) startPos = new Vector2Int(x, y); // 플레이어는 1층에 있어야 함
                            id = 0; // 데이터 상으로는 0(빈 공간)
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

    void CreateObj(int id, int var, Vector3 pos, int layer, int x, int y) {
        TileData d = GetTileData(id);
        if (d != null) {
            VisualVariant v = d.GetVariantByWeight(var);
            if (v.prefab != null) {
                GameObject go = Instantiate(v.prefab, pos, Quaternion.Euler(v.rotation));
                if(v.overrideMat != null) {
                    Renderer r = go.GetComponentInChildren<Renderer>();
                    if(r) r.material = v.overrideMat;
                }
                go.transform.parent = transform;
                
                float hm = (layer == 0) ? 1.0f : d.heightMultiplier;
                float h = tileHeight * hm;
                go.transform.localScale = new Vector3(tileSizeXZ, h, tileSizeXZ);
                
                float yOff = (layer == 0) ? 0 : (layer == 1 ? tileHeight : tileHeight * 3); // 층 간격 (2층은 좀 더 높게)
                go.transform.position += Vector3.up * (yOff + h/2f);

                if(layer == 1) objMap[x, y] = go; 
            }
        }
    }

    void SyncVisuals() {
        if(objPlayer != null) UpdatePlayerVis();
        for(int x=0; x<width; x++) {
            for(int y=0; y<height; y++) {
                if(objMap[x,y] != null) {
                    int id = gridSystem.GetLayerID(x, y, 1); // 1층 확인
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
        // 플레이어는 1층 높이(tileHeight) 위에 서야 함
        objPlayer.transform.position = new Vector3(i.x - oX, tileHeight, i.y - oZ);
    }

    void RotatePlayer(int dx, int dy) {
        if(objPlayer && (dx!=0 || dy!=0)) 
            objPlayer.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dy));
    }

    TileData GetTileData(int id) { foreach(var d in tilePalette) if(d.tileID == id) return d; return null; }
    void ClearMapVisuals() { if(objMap!=null) foreach(var o in transform) if(o!=objPlayer.transform && o!=transform) Destroy(((Transform)o).gameObject); }
    IEnumerator ProcessFail() { isGameEnding=true; uiManager?.ShowFail(); yield return new WaitForSeconds(1.5f); InitializeGame(); }
    IEnumerator ProcessClear() { isGameEnding=true; uiManager?.ShowClear(); yield return new WaitForSeconds(1.5f); currentLevelIndex++; InitializeGame(); }
    void AutoAdjustCamera() { Camera.main.transform.position = -Camera.main.transform.forward * 50f; }
}