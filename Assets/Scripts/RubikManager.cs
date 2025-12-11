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
        this.playerRot = rot; 
    }
}

public class RubikManager : MonoBehaviour
{
    public GridSystem gridSystem;
    public UIManager uiManager;
    public TileData[] tilePalette;
    
    public TextAsset[] stageFiles; 
    public int currentStageIndex = 0; 
    
    public GameObject prefabPlayer;
    [Range(0.5f, 1.0f)] public float tileSizeXZ = 0.8f;
    [Range(0.1f, 3.0f)] public float tileHeight = 0.2f;

    [Header("게임 규칙")]
    public int maxShiftCount = 10; 
    private int _currentShifts;

    [Header("애니메이션 설정")]
    public float moveDuration = 0.2f; 
    private bool isAnimating = false; 

    private Stack<GameState> undoStack = new Stack<GameState>();

    private GameObject[,] objMap;
    private GameObject objPlayer;
    private bool isGameEnding = false;
    private float _lastSizeXZ, _lastHeight;
    private int width, height;

    [Header("오디오 시스템")]
    public AudioSource audioSource; 
    public AudioSource bgmSource;   
    public AudioClip bgmClip;

    [Header("기본 사운드")]
    public AudioClip defaultWalk;    
    public AudioClip defaultPush;    
    public AudioClip defaultDestroy; 

    [Header("게임 상태 사운드")]
    public AudioClip clipFail;       
    public AudioClip clipClear;      
    public AudioClip clipAllClear;   
    public AudioClip clipShift;      

    void Start() {
        if (gridSystem == null) gridSystem = GetComponent<GridSystem>();
        
        gridSystem.OnMapChanged += UpdateView;
        gridSystem.OnPlayerMoved += UpdatePlayerVis;
        gridSystem.OnTrapTriggered += ProcessFail; 
        gridSystem.OnGoalTriggered += () => StartCoroutine(ProcessClear());

        gridSystem.OnSoundWalk += (tile) => PlayTileSound(tile, SoundType.Walk);
        gridSystem.OnSoundPush += (tile) => PlayTileSound(tile, SoundType.Push);
        gridSystem.OnSoundDestroy += (tile) => PlayTileSound(tile, SoundType.Destroy);
        
        InitializeGame();
        PlayBGM();
    }

    void Update() {
        if (tileSizeXZ != _lastSizeXZ || tileHeight != _lastHeight) {
            SyncVisuals(); _lastSizeXZ = tileSizeXZ; _lastHeight = tileHeight;
        }
    }

    void InitializeGame() {
        StopAllCoroutines(); 
        isGameEnding = false;
        isAnimating = false;
        
        undoStack.Clear();

        if (uiManager != null) uiManager.HideAll();
        ClearMapVisuals();
        
        LoadStage(); 
        
        _currentShifts = maxShiftCount;
        if (uiManager != null) 
            uiManager.UpdateShiftText(_currentShifts, maxShiftCount);
        
        Debug.Log($"스테이지 {currentStageIndex + 1} 시작! 남은 회전 횟수: {_currentShifts}");

        UpdateView(); 
        UpdatePlayerVis(); 
        AutoAdjustCamera();
    }

    // --- [오디오 시스템] ---
    
    private enum SoundType { Walk, Push, Destroy }

    void PlayTileSound(TileData tile, SoundType type)
    {
        AudioClip clipToPlay = null;

        if (tile != null)
        {
            switch (type)
            {
                case SoundType.Walk: clipToPlay = tile.clipStep; break;
                case SoundType.Push: clipToPlay = tile.clipPush; break;
                case SoundType.Destroy: clipToPlay = tile.clipDestroy; break;
            }
        }

        if (clipToPlay == null)
        {
            switch (type)
            {
                case SoundType.Walk: clipToPlay = defaultWalk; break;
                case SoundType.Push: clipToPlay = defaultPush; break;
                case SoundType.Destroy: clipToPlay = defaultDestroy; break;
            }
        }

        PlaySFX(clipToPlay);
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(clip);
        }
    }

    void PlayBGM()
    {
        if (bgmSource != null && bgmClip != null)
        {
            bgmSource.clip = bgmClip;
            bgmSource.loop = true;
            bgmSource.Play();
        }
    }

    // --- [게임 로직] ---

    void ProcessFail() 
    { 
        if(!isGameEnding) {
            isGameEnding = true;   
            uiManager?.ShowFail(); 
            PlaySFX(clipFail);
        }
    }
    
    IEnumerator ProcessClear() 
    { 
        if(!isGameEnding) {
            isGameEnding = true; 
            PlaySFX(clipClear);
            uiManager?.ShowClear(); 
            
            yield return new WaitForSeconds(1.5f); 
            
            currentStageIndex++; 

            if (currentStageIndex >= stageFiles.Length) 
            {
                Debug.Log("모든 스테이지 클리어! 축하합니다!");
                if(clipAllClear != null) PlaySFX(clipAllClear);
                uiManager?.ShowAllClear();
            }
            else 
            {
                InitializeGame();
            }
        }
    }

    // --- [입력 및 Undo] ---

    void SaveState()
    {
        int[,,] currentMap = gridSystem.GetMapSnapshot();
        Vector2Int currentPos = gridSystem.PlayerIndex;
        Quaternion currentRot = objPlayer != null ? objPlayer.transform.rotation : Quaternion.identity;

        undoStack.Push(new GameState(currentMap, currentPos, _currentShifts, currentRot));
    }

    public void OnClickUndo()
    {
        if (isAnimating || undoStack.Count == 0) return;

        GameState lastState = undoStack.Pop();
        _currentShifts = lastState.remainingShifts;
        gridSystem.RestoreMapData(lastState.mapData, lastState.playerPos);

        if (objPlayer != null) objPlayer.transform.rotation = lastState.playerRot;

        isGameEnding = false;
        if (uiManager != null) uiManager.HideAll();

        UpdateView(); 
        UpdatePlayerVis(); 
        if (uiManager != null) uiManager.UpdateShiftText(_currentShifts, maxShiftCount);
    }

    public void OnClickReset()
    {
        InitializeGame();
    }

    public void TryMovePlayer(int dx, int dy) { 
        if(!isGameEnding && !isAnimating) { 
            SaveState(); 
            Quaternion beforeRot = objPlayer != null ? objPlayer.transform.rotation : Quaternion.identity;
            RotatePlayer(dx, dy); 
            Quaternion afterRot = objPlayer != null ? objPlayer.transform.rotation : Quaternion.identity;

            bool isMoved = gridSystem.TryMovePlayer(dx, dy);

            if (!isMoved && beforeRot == afterRot)
            {
                undoStack.Pop(); 
            }
        } 
    }

    // --- [맵 회전 입력 및 애니메이션] ---

    public void TryPushRow(int dir) 
    { 
        if (isAnimating || isGameEnding || _currentShifts <= 0) return;
        
        SaveState(); 
        isAnimating = true; 
        
        Vector2Int oldPlayerPos = gridSystem.PlayerIndex;
        bool success = gridSystem.TryPushRow(dir); 

        if (success)
        {
            PlaySFX(clipShift);
            bool playerActuallyMoved = (gridSystem.PlayerIndex != oldPlayerPos);
            StartCoroutine(AnimateRow(oldPlayerPos.y, dir, playerActuallyMoved));
        }
        else
        {
            isAnimating = false;
            undoStack.Pop();
        }
    }

    public void TryPushCol(int dir) 
    { 
        if (isAnimating || isGameEnding || _currentShifts <= 0) return;
        
        SaveState();
        isAnimating = true;

        Vector2Int oldPlayerPos = gridSystem.PlayerIndex;
        bool success = gridSystem.TryPushCol(dir);

        if (success)
        {
            PlaySFX(clipShift);
            bool playerActuallyMoved = (gridSystem.PlayerIndex != oldPlayerPos);
            StartCoroutine(AnimateCol(oldPlayerPos.x, dir, playerActuallyMoved));
        }
        else
        {
            isAnimating = false;
            undoStack.Pop();
        }
    }
    
    int PlayerIndexX() => gridSystem.PlayerIndex.x;
    int PlayerIndexY() => gridSystem.PlayerIndex.y;

    IEnumerator AnimateRow(int y, int dir, bool movePlayer)
    {
        int visualDir = -dir; 

        List<Transform> movingObjs = new List<Transform>();
        for (int x = 0; x < width; x++)
        {
            if (objMap[x, y] != null) movingObjs.Add(objMap[x, y].transform);
        }
        
        if (movePlayer && objPlayer != null)
        {
            movingObjs.Add(objPlayer.transform);
        }

        GameObject ghostObj = null;
        int wrapIndex = (visualDir == 1) ? width - 1 : 0; 
        
        if (objMap[wrapIndex, y] != null)
        {
            ghostObj = Instantiate(objMap[wrapIndex, y]); 
            float startX = (visualDir == 1) ? -1 : width;
            float oX = width / 2f - 0.5f;
            float oZ = height / 2f - 0.5f;
            ghostObj.transform.position = new Vector3(startX - oX, 0, y - oZ); 
            movingObjs.Add(ghostObj.transform);
        }

        float elapsed = 0f;
        Dictionary<Transform, Vector3> startPositions = new Dictionary<Transform, Vector3>();
        Dictionary<Transform, Vector3> endPositions = new Dictionary<Transform, Vector3>();

        foreach (var t in movingObjs)
        {
            startPositions[t] = t.position;
            endPositions[t] = t.position + new Vector3(visualDir * tileSizeXZ, 0, 0); 
        }

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            t = t * t * (3f - 2f * t); 

            foreach (var tr in movingObjs)
            {
                if (tr != null)
                    tr.position = Vector3.Lerp(startPositions[tr], endPositions[tr], t);
            }
            yield return null;
        }

        if (ghostObj != null) Destroy(ghostObj);

        UseShiftChance(); // ★ 여기가 에러 나던 곳 -> 아래 함수 추가로 해결
        isAnimating = false;
        
        UpdateView(); 
        UpdatePlayerVis();
    }

    IEnumerator AnimateCol(int x, int dir, bool movePlayer)
    {
        int visualDir = -dir;

        List<Transform> movingObjs = new List<Transform>();
        for (int y = 0; y < height; y++)
        {
            if (objMap[x, y] != null) movingObjs.Add(objMap[x, y].transform);
        }

        if (movePlayer && objPlayer != null)
            movingObjs.Add(objPlayer.transform);

        GameObject ghostObj = null;
        int wrapIndex = (visualDir == 1) ? height - 1 : 0; 

        if (objMap[x, wrapIndex] != null)
        {
            ghostObj = Instantiate(objMap[x, wrapIndex]);
            float startY = (visualDir == 1) ? -1 : height;
            float oX = width / 2f - 0.5f;
            float oZ = height / 2f - 0.5f;
            ghostObj.transform.position = new Vector3(x - oX, 0, startY - oZ);
            movingObjs.Add(ghostObj.transform);
        }

        float elapsed = 0f;
        Dictionary<Transform, Vector3> startPos = new Dictionary<Transform, Vector3>();
        Dictionary<Transform, Vector3> endPos = new Dictionary<Transform, Vector3>();

        foreach (var t in movingObjs)
        {
            startPos[t] = t.position;
            endPos[t] = t.position + new Vector3(0, 0, visualDir * tileSizeXZ); 
        }

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            t = t * t * (3f - 2f * t);

            foreach (var tr in movingObjs)
            {
                if (tr != null)
                    tr.position = Vector3.Lerp(startPos[tr], endPos[tr], t);
            }
            yield return null;
        }

        if (ghostObj != null) Destroy(ghostObj);

        UseShiftChance(); // ★ 여기가 에러 나던 곳 -> 아래 함수 추가로 해결
        isAnimating = false;
        
        UpdateView(); 
        UpdatePlayerVis();
    }

    // ★ [추가된 함수] 누락되었던 횟수 차감 및 UI 갱신 함수
    void UseShiftChance()
    {
        _currentShifts--;
        if (uiManager != null) 
            uiManager.UpdateShiftText(_currentShifts, maxShiftCount);
        Debug.Log($"회전 성공! 남은 횟수: {_currentShifts}");
    }

    // --- [맵 로딩 및 시각화] ---

    void LoadStage()
    {
        if (stageFiles == null || stageFiles.Length == 0) return;
        if (currentStageIndex >= stageFiles.Length) return; 
        
        string jsonText = stageFiles[currentStageIndex].text;
        
        StageDataRoot data = null;
        try { data = JsonConvert.DeserializeObject<StageDataRoot>(jsonText); }
        catch (System.Exception e) { Debug.LogError("JSON 파싱 실패: " + e.Message); return; }

        if (data == null || data.properties == null) return;

        width = data.properties.width;
        height = data.properties.height;
        maxShiftCount = data.properties.maxShifts;
        
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
        if (isAnimating) return; 

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
        List<GameObject> objectsToDestroy = new List<GameObject>();

        foreach(Transform child in transform) {
            if (objPlayer != null && child == objPlayer.transform) continue;
            if (child.name == "Audio_BGM" || child.name == "Audio_SFX") continue;

            objectsToDestroy.Add(child.gameObject);
        }

        foreach(GameObject go in objectsToDestroy) {
            Destroy(go);
        }
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