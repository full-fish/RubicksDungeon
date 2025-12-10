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

void CreateObj(int id, int var, Vector3 pos, int layer, int x, int y)
    {
        TileData d = GetTileData(id);
        if (d != null)
        {
            VisualVariant v = d.GetVariantByWeight(var);
            if (v.prefab != null)
            {
                GameObject go = Instantiate(v.prefab, pos, Quaternion.Euler(v.rotation));
                
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
                foreach (Renderer r in renderers)
                {
                    if (v.overrideMat != null) r.material = v.overrideMat;
                }

                go.transform.parent = transform;
                
                // 높이 설정
                float hm = (layer == 0) ? 1.0f : d.heightMultiplier;
                float h = tileHeight * hm;
                float xzScale = (layer == 0) ? 1.0f : tileSizeXZ;
                
                go.transform.localScale = new Vector3(xzScale, h, xzScale);
                
                // 위치 설정 (발바닥 피벗 기준)
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
        float playerY = 0f;
        objPlayer.transform.position = new Vector3(i.x - oX, playerY, i.y - oZ);
    }

    void RotatePlayer(int dx, int dy) {
        if(objPlayer && (dx!=0 || dy!=0)) 
            objPlayer.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dy));
    }

    TileData GetTileData(int id) { foreach(var d in tilePalette) if(d.tileID == id) return d; return null; }
    void ClearMapVisuals() { 
        if(objMap!=null) 
        {
            foreach(var o in transform) 
            {
                // o를 UnityEngine.Object 타입으로 명시적으로 변환하여 유니티 비교를 강제합니다.
                if ((UnityEngine.Object)o != objPlayer.transform && (UnityEngine.Object)o != transform) 
                {
                    Destroy(((Transform)o).gameObject);
                }
            }
        }
    }
    IEnumerator ProcessFail() { isGameEnding=true; uiManager?.ShowFail(); yield return new WaitForSeconds(1.5f); InitializeGame(); }
    IEnumerator ProcessClear() { isGameEnding=true; uiManager?.ShowClear(); yield return new WaitForSeconds(1.5f); currentLevelIndex++; InitializeGame(); }
    void AutoAdjustCamera() { 
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = -cam.transform.forward * 50f;
        
        // 1. 맵의 실제 물리적 크기 계산 (타일 개수 * 간격)
        float mapW = width * tileSizeXZ;
        float mapH = height * tileSizeXZ; // Z축 길이지만 화면상 높이로 취급

        // 2. 여유 공간 (Padding) - 너무 꽉 차면 답답하니까
        float padding = 2.0f;  

        // 3. 카메라 설정에 따라 다르게 처리
        if (cam.orthographic)
        {
            // [Orthographic 모드] Size 값을 조절
            float vertSize = (mapH / 2f) + padding;
            float horzSize = ((mapW / 2f) + padding) / cam.aspect; // 화면 비율(aspect) 고려
            
            // 세로와 가로 중 더 큰 쪽에 맞춤
            cam.orthographicSize = Mathf.Max(vertSize, horzSize);
        }
        else
        {
            // [Perspective 모드] 카메라를 뒤로(Z축) 뺌
            // 맵의 대각선 길이를 기준으로 적절한 거리를 계산합니다.
            float targetSize = Mathf.Max(mapW, mapH) + padding * 2;
            
            // 시야각(FOV)에 따른 거리 계산 공식 (대략적)
            // 거리가 멀어질수록 많이 보임
            float distance = targetSize / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            
            // 카메라가 바라보는 방향(Forward)의 반대편으로 이동
            // 45도 각도로 보고 있다면, 그 각도 그대로 뒤로 쭉 물러납니다.
            // cam.transform.position = Vector3.zero - (cam.transform.forward * distance);
        }
         }
}