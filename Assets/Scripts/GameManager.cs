using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json; 

public class GameManager : MonoBehaviour
{
    [Header("Managers")]
    public GridSystem gridSystem;
    public AudioManager audioManager;
    public VisualManager visualManager;
    public UIManager uiManager;
    
    [Header("Data")]
    public TileData[] tilePalette;
    public TextAsset[] stageFiles;
    public GameObject prefabPlayer;

    [Header("Rules")]
    public int maxShiftCount = 10;
    private int _currentShifts;
    public int currentStageIndex = 0;

    private Stack<GameState> undoStack = new Stack<GameState>();
    private bool isGameEnding = false;

    void Start() 
    {
        // 의존성 연결 (없는 경우 GetComponent로 찾기)
        if (!gridSystem) gridSystem = GetComponentInChildren<GridSystem>();
        if (!audioManager) audioManager = GetComponentInChildren<AudioManager>();
        if (!visualManager) visualManager = GetComponentInChildren<VisualManager>();

        // 이벤트 연결
        gridSystem.OnTrapTriggered += ProcessFail; 
        gridSystem.OnGoalTriggered += () => StartCoroutine(ProcessClear());

        gridSystem.OnSoundWalk += (t) => audioManager.PlayTileSound(t, SoundType.Walk);
        gridSystem.OnSoundPush += (t) => audioManager.PlayTileSound(t, SoundType.Push);
        gridSystem.OnSoundDestroy += (t) => audioManager.PlayTileSound(t, SoundType.Destroy);
        gridSystem.OnSoundShift += () => audioManager.PlaySFX(audioManager.clipShift);

        audioManager.Init();
        InitializeGame();
    }

    void InitializeGame()
    {
        StopAllCoroutines(); 
        isGameEnding = false;
        undoStack.Clear();
        uiManager?.HideAll();
        
        LoadStage(); 
        
        _currentShifts = maxShiftCount;
        uiManager?.UpdateShiftText(_currentShifts, maxShiftCount);
        
        // 화면 초기화
        visualManager.RefreshView();
    }

    void LoadStage()
    {
        if (stageFiles == null || currentStageIndex >= stageFiles.Length) return;
        
        var data = JsonConvert.DeserializeObject<StageDataRoot>(stageFiles[currentStageIndex].text);
        if (data == null) return;

        int w = data.properties.width;
        int h = data.properties.height;
        maxShiftCount = data.properties.maxShifts;
        
        int[,,] maps = new int[w, h, 3];
        Vector2Int startPos = Vector2Int.zero;

        FillLayer(maps, data.layers.tile, 0, ref startPos, w, h);   
        FillLayer(maps, data.layers.ground, 1, ref startPos, w, h); 
        FillLayer(maps, data.layers.sky, 2, ref startPos, w, h);    

        gridSystem.Initialize(w, h, maps, tilePalette, startPos);
        visualManager.Init(gridSystem, w, h, prefabPlayer);
        Debug.Log($"Stage Loaded: {data.properties.stageName}");
    }

    void FillLayer(int[,,] map, int[][] src, int l, ref Vector2Int sPos, int w, int h) {
        if (src == null) return;
        for (int r = 0; r < src.Length; r++) {
            if (src[r] == null) continue;
            for (int c = 0; c < src[r].Length; c++) {
                int x = c; int y = h - 1 - r;
                if (x < w && y >= 0) {
                    int id = (src[r][c] == -1) ? 0 : src[r][c];
                    map[x, y, l] = id;
                    if (l == 1 && src[r][c] == 0) sPos = new Vector2Int(x, y);
                }
            }
        }
    }

    // --- 입력 처리 ---
    public void TryMovePlayer(int dx, int dy) 
    { 
        if(isGameEnding || visualManager.IsAnimating) return;
        visualManager.RotatePlayer(dx, dy); 

        SaveState(); 
        Vector2Int oldPos = gridSystem.PlayerIndex;
        Quaternion oldRot = visualManager.transform.GetComponentInChildren<PlayerController>().transform.rotation;

        if (gridSystem.TryMovePlayer(dx, dy)) {
            StartCoroutine(visualManager.AnimateMoveRoutine(oldPos, dx, dy));
        } else {
            // 회전만 하고 이동 못했으면 Undo 유지, 회전도 안했으면 취소
            // (간소화를 위해 여기선 실패시 그냥 취소)
            undoStack.Pop(); 
        }
    }

    public void TryPushRow(int dir) => TryShift(true, gridSystem.PlayerIndex.y, dir);
    public void TryPushCol(int dir) => TryShift(false, gridSystem.PlayerIndex.x, dir);

    void TryShift(bool isRow, int index, int dir)
    {
        if(isGameEnding || visualManager.IsAnimating || _currentShifts <= 0) return;
        
        SaveState();
        Vector2Int oldPos = gridSystem.PlayerIndex;
        
        bool success = isRow ? gridSystem.TryPushRow(dir) : gridSystem.TryPushCol(dir);
        
        if (success) {
            UseShiftChance();
            bool playerMoved = (gridSystem.PlayerIndex != oldPos);
            StartCoroutine(visualManager.AnimateShiftRoutine(isRow, index, dir, playerMoved));
        } else {
            undoStack.Pop();
        }
    }

    void UseShiftChance() {
        _currentShifts--;
        uiManager?.UpdateShiftText(_currentShifts, maxShiftCount);
    }

    // --- 공통 기능 ---
    void SaveState() {
        // PlayerController transform을 찾아서 회전값 저장
        Transform pt = visualManager.transform.GetComponentInChildren<PlayerController>()?.transform;
        Quaternion rot = pt ? pt.rotation : Quaternion.identity;
        undoStack.Push(new GameState(gridSystem.GetMapSnapshot(), gridSystem.PlayerIndex, _currentShifts, rot));
    }

    public void OnClickUndo() {
        if (visualManager.IsAnimating || undoStack.Count == 0) return;
        GameState state = undoStack.Pop();
        _currentShifts = state.remainingShifts;
        gridSystem.RestoreMapData(state.mapData, state.playerPos);
        isGameEnding = false;
        uiManager?.HideAll();
        visualManager.RefreshView();
        
        // 플레이어 회전 복구
        Transform pt = visualManager.transform.GetComponentInChildren<PlayerController>()?.transform;
        if(pt) pt.rotation = state.playerRot;
        
        uiManager?.UpdateShiftText(_currentShifts, maxShiftCount);
    }

    public void OnClickReset() => InitializeGame();

    void ProcessFail() {
        if(!isGameEnding) {
            isGameEnding = true;
            uiManager?.ShowFail();
            audioManager.PlaySFX(audioManager.clipFail);
        }
    }

    IEnumerator ProcessClear() {
        if(!isGameEnding) {
            isGameEnding = true;
            audioManager.PlaySFX(audioManager.clipClear);
            uiManager?.ShowClear();
            yield return new WaitForSeconds(1.5f);
            
            currentStageIndex++;
            if (currentStageIndex >= stageFiles.Length) {
                uiManager?.ShowAllClear();
                audioManager.PlaySFX(audioManager.clipAllClear);
            } else {
                InitializeGame();
            }
        }
    }
}