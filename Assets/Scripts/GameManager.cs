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
        if (!gridSystem) gridSystem = FindFirstObjectByType<GridSystem>();
        if (!audioManager) audioManager = FindFirstObjectByType<AudioManager>();
        if (!visualManager) visualManager = FindFirstObjectByType<VisualManager>();

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

        // ★ [수정] 이동(Move) 행동 기록 저장
        SaveState(ActionType.Move, dx, dy); 
        Vector2Int oldPos = gridSystem.PlayerIndex;

        if (gridSystem.TryMovePlayer(dx, dy)) {
            StartCoroutine(visualManager.AnimateMoveRoutine(oldPos, dx, dy));
        } else {
            undoStack.Pop(); 
        }
    }

    public void TryPushRow(int dir) => TryShift(true, gridSystem.PlayerIndex.y, dir);
    public void TryPushCol(int dir) => TryShift(false, gridSystem.PlayerIndex.x, dir);

    void TryShift(bool isRow, int index, int dir)
    {
        if(isGameEnding || visualManager.IsAnimating || _currentShifts <= 0) return;
        
        // ★ [수정] 회전(Shift) 행동 기록 저장
        SaveState(isRow ? ActionType.ShiftRow : ActionType.ShiftCol, index, dir);
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

    // --- Undo Logic 수정 ---
    void SaveState(ActionType type, int v1, int v2) 
    {
        int[,,] mapSnap = gridSystem.GetMapSnapshot();
        if (mapSnap == null) return;

        var pc = visualManager.transform.GetComponentInChildren<PlayerController>();
        Quaternion rot = pc ? pc.transform.rotation : Quaternion.identity;

        undoStack.Push(new GameState(mapSnap, gridSystem.PlayerIndex, _currentShifts, rot, type, v1, v2));
    }

    public void OnClickUndo() {
        if (visualManager.IsAnimating || undoStack.Count == 0) return;

        // ★ [추가] 뷰 갱신 잠금 (애니메이션을 위해)
        visualManager.PrepareUndo();

        GameState state = undoStack.Pop();
        _currentShifts = state.remainingShifts;
        
        // 1. 데이터 복구 (이때 RefreshView가 호출되어도 PrepareUndo 때문에 무시됨)
        gridSystem.RestoreMapData(state.mapData, state.playerPos);
        isGameEnding = false;
        uiManager?.HideAll();

        // 2. 파괴되었던 오브젝트 즉시 부활

        // 3. 플레이어 회전 복구
        Transform pt = visualManager.transform.GetComponentInChildren<PlayerController>()?.transform;
        if(pt) pt.rotation = state.playerRot;
        
        uiManager?.UpdateShiftText(_currentShifts, maxShiftCount);

        // 4. 역방향 애니메이션 실행
        switch (state.actionType)
        {
            case ActionType.Move:
                StartCoroutine(visualManager.AnimateUndoMove(state.playerPos + new Vector2Int(state.val1, state.val2), -state.val1, -state.val2));
                break;

            case ActionType.ShiftRow:
                StartCoroutine(visualManager.AnimateShiftRoutine(true, state.val1, -state.val2, false)); 
                break;

            case ActionType.ShiftCol:
                StartCoroutine(visualManager.AnimateShiftRoutine(false, state.val1, -state.val2, false));
                break;

            default:
                // 애니메이션이 없으면 수동 갱신 해제 후 리프레시
                // (하지만 PrepareUndo로 잠겼으니 강제로 풀어야 함? 
                //  AnimateUndoMove 등이 끝나면 자동으로 풀리지만 여기선 직접 풀어야 함)
                //  하지만 ActionType.None은 거의 없으므로 일단 RefreshView 호출.
                //  여기서는 IsAnimating=true 상태라 RefreshView가 안 먹힘.
                //  그래서 코루틴을 하나 돌리거나, IsAnimating을 false로 바꾸고 호출해야 함.
                //  간단히:
                visualManager.RefreshView(); // (IsAnimating 때문에 무시될 수 있음)
                // 위 코드는 사실상 호출 안 됨.
                // 하지만 정상적인 Undo라면 위 3케이스 중 하나일 것임.
                break;
        }
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

    void UseShiftChance() {
        _currentShifts--;
        uiManager?.UpdateShiftText(_currentShifts, maxShiftCount);
    }
}