using UnityEngine;
using System; 
using System.Collections; 

public class RubikManager : MonoBehaviour
{
    [Header("UI 매니저 연결")]
    public UIManager uiManager;

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

    private int width;
    private int height;
    private int[,] mapData;
    private Vector2Int playerIndex;
    
    private GameObject[,] objMap;
    private GameObject objPlayer;

    private float _lastSizeXZ;
    private float _lastHeight;

    private bool isGameEnding = false;

    void Start() => InitializeGame();

    void Update()
    {
        if (reloadMap)
        {
            reloadMap = false;
            InitializeGame();
        }

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
        LoadMapFromFile(); 
        UpdateView();
        UpdatePlayerPosition();
        AutoAdjustCamera();

        _lastSizeXZ = tileSizeXZ;
        _lastHeight = tileHeight;
    }

    public void TryMovePlayer(int dx, int dy)
    {
        if (isGameEnding) return;

        int nextX = playerIndex.x + dx;
        int nextY = playerIndex.y + dy;

        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height) return;
        if (mapData[nextX, nextY] == 1) return; 

        playerIndex = new Vector2Int(nextX, nextY);
        UpdatePlayerPosition();
        CheckFoot();
    }

    public void TryPushRow(int dir)
    {
        if (isGameEnding) return;
        if (CanPushRow(playerIndex.y, dir)) ShiftRow(playerIndex.y, -dir); 
    }

    public void TryPushCol(int dir)
    {
        if (isGameEnding) return;
        if (CanPushCol(playerIndex.x, dir)) ShiftCol(playerIndex.x, -dir);
    }

    void CheckFoot()
    {
        int type = mapData[playerIndex.x, playerIndex.y];

        if (type == 2) StartCoroutine(ProcessFailSequence());
        else if (type == 4) StartCoroutine(ProcessClearSequence());
    }

    IEnumerator ProcessFailSequence()
    {
        isGameEnding = true; 
        Debug.Log("함정! 재시작");
        if (uiManager != null) uiManager.ShowFail();
        yield return new WaitForSeconds(1.5f);
        InitializeGame(); 
    }

    IEnumerator ProcessClearSequence()
    {
        isGameEnding = true; 
        Debug.Log("클리어! 다음 레벨");
        if (uiManager != null) uiManager.ShowClear();
        yield return new WaitForSeconds(1.5f);
        currentLevelIndex++; 
        InitializeGame(); 
    }

    void LoadMapFromFile()
    {
        if (levelFiles == null || levelFiles.Length == 0) return;
        if (currentLevelIndex >= levelFiles.Length) currentLevelIndex = 0; 
        
        string[] lines = levelFiles[currentLevelIndex].text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        height = lines.Length; 
        width = lines[0].Trim().Split(' ').Length; 

        mapData = new int[width, height];
        objMap = new GameObject[width, height];

        for (int y = 0; y < height; y++)
        {
            string[] numbers = lines[height - 1 - y].Trim().Split(' '); 
            for (int x = 0; x < width; x++)
            {
                if (x < numbers.Length) mapData[x, y] = int.Parse(numbers[x]);
            }
        }
        playerIndex = new Vector2Int(width / 2, height / 2);
        mapData[playerIndex.x, playerIndex.y] = 0;
    }

    void ShiftRow(int y, int dir)
    {
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

    void ShiftCol(int x, int dir)
    {
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
                GameObject prefab = prefabFloor;
                int t = mapData[x, y];
                if (t == 1) prefab = prefabWall;
                else if (t == 2) prefab = prefabTrap;
                else if (t == 3) prefab = prefabFloorDetail;
                else if (t == 4) prefab = prefabChest;

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
            var playerScript = objPlayer.GetComponent<RubikPlayer>();
            if (playerScript == null) playerScript = objPlayer.AddComponent<RubikPlayer>();
            playerScript.Init(this); 
        }
        
        objPlayer.transform.localScale = Vector3.one;
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
        cam.orthographicSize = Mathf.Max(width, height) * 0.6f + 2f; 
        cam.transform.position = -cam.transform.forward * 50f;
    }
}