using UnityEngine;
using System.Collections;
using System;
// 게임 전체 흐름과 화면 표시(Visual)만 담당하는 클래스
public class RubikManager : MonoBehaviour
{
    [Header("핵심 연결")]
    public GridSystem gridSystem; // ★ 여기에 GridSystem 오브젝트 연결
    public UIManager uiManager;

    [Header("데이터")]
    public TileData[] tilePalette;
    public TextAsset[] levelFiles; 
    public int currentLevelIndex = 0; 

    [Header("비주얼")]
    public GameObject prefabPlayer;
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    // 화면 표시용 객체들
    private GameObject[,] objMap;
    private GameObject objPlayer;
    private bool isGameEnding = false;

    // 매니저가 GridSystem의 정보를 읽어올 때 쓸 변수들
    private int width;
    private int height;

    void Start()
    {
        // GridSystem이 없으면 내 몸에서 찾음
        if (gridSystem == null) gridSystem = GetComponent<GridSystem>();
        
        // GridSystem의 이벤트 구독 (신호 연결)
        gridSystem.OnMapChanged += UpdateView;       // 맵 변하면 -> 다시 그려라
        gridSystem.OnPlayerMoved += UpdatePlayerVis; // 이동하면 -> 플레이어 옮겨라
        gridSystem.OnTrapTriggered += () => StartCoroutine(ProcessFail());
        gridSystem.OnGoalTriggered += () => StartCoroutine(ProcessClear());

        InitializeGame();
    }

    void InitializeGame()
    {
        StopAllCoroutines();
        isGameEnding = false;
        if (uiManager != null) uiManager.HideAll();

        ClearMapVisuals();
        LoadMapAndInitGrid(); // 맵 파일 읽고 GridSystem 초기화

        UpdateView();
        UpdatePlayerVis();
        AutoAdjustCamera();
    }

    // --- [중요] 플레이어의 입력(RubikPlayer)을 받아서 GridSystem에 전달 ---
    public void TryMovePlayer(int dx, int dy) { if(!isGameEnding) gridSystem.TryMovePlayer(dx, dy); }
    public void TryPushRow(int dir)           { if(!isGameEnding) gridSystem.TryPushRow(dir); }
    public void TryPushCol(int dir)           { if(!isGameEnding) gridSystem.TryPushCol(dir); }

    // --- 파일 로딩 및 초기화 ---
    void LoadMapAndInitGrid()
    {
        if (levelFiles == null || levelFiles.Length == 0) return;
        if (currentLevelIndex >= levelFiles.Length) currentLevelIndex = 0;

        string[] lines = levelFiles[currentLevelIndex].text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        height = lines.Length; 
        width = lines[0].Trim().Split(' ').Length; 

        int[,] mapData = new int[width, height];
        objMap = new GameObject[width, height];

        for (int y = 0; y < height; y++)
        {
            string[] numbers = lines[height - 1 - y].Trim().Split(' '); 
            for (int x = 0; x < width; x++)
            {
                if (x < numbers.Length) mapData[x, y] = int.Parse(numbers[x]);
            }
        }
        Vector2Int startPos = new Vector2Int(width / 2, height / 2);
        mapData[startPos.x, startPos.y] = 0; // 시작 위치 바닥으로

        // ★ 데이터를 만들어서 GridSystem에게 넘겨줌 (초기화)
        gridSystem.Initialize(width, height, mapData, tilePalette, startPos);
    }

    // --- 비주얼 업데이트 (GridSystem의 데이터를 보고 그림) ---
    void UpdateView()
    {
        ClearMapVisuals();
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // ★ GridSystem에게 "여기 무슨 타일이야?" 물어봄
                int id = gridSystem.GetTileID(x, y);
                TileData data = GetTileData(id);

                if (data != null && data.prefab != null)
                {
                    Vector3 pos = new Vector3(x - offsetX, 0, y - offsetZ);
                    GameObject newObj = Instantiate(data.prefab, pos, Quaternion.identity);
                    newObj.transform.parent = transform;
                    newObj.transform.localScale = new Vector3(tileSizeXZ, tileHeight, tileSizeXZ);
                    newObj.transform.position += Vector3.up * (tileHeight / 2f);
                    objMap[x, y] = newObj;
                }
            }
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
        
        // ★ GridSystem에게 "플레이어 어디 있어?" 물어봄
        Vector2Int idx = gridSystem.PlayerIndex;
        
        float offsetX = width / 2f - 0.5f;
        float offsetZ = height / 2f - 0.5f;
        Vector3 pos = new Vector3(idx.x - offsetX, tileHeight, idx.y - offsetZ);
        objPlayer.transform.position = pos;
    }

    TileData GetTileData(int id)
    {
        foreach (var data in tilePalette) if (data.tileID == id) return data;
        return null;
    }

    // --- 게임 오버/클리어 연출 ---
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
}