using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class VisualManager : MonoBehaviour
{
    [Header("설정")]
    public float tileSizeXZ = 0.8f;
    public float tileHeight = 0.2f;
    public float moveDuration = 0.2f;

    private GameObject[,] objMap;
    private GameObject objPlayer;
    private GridSystem _grid;
    private int width, height;

    public bool IsAnimating { get; private set; }

    public void Init(GridSystem grid, int w, int h, GameObject prefabPlayer)
    {
        _grid = grid;
        width = w;
        height = h;
        objMap = new GameObject[w, h];
        
        if (objPlayer == null && prefabPlayer != null) {
            objPlayer = Instantiate(prefabPlayer);
            objPlayer.transform.parent = transform;
        }
        AutoAdjustCamera();
    }

    // ★ [추가] Undo 직전에 호출해서 화면 갱신을 잠시 막음
    public void PrepareUndo() 
    {
        IsAnimating = true; 
    }

    public void RefreshView()
    {
        if (IsAnimating) return;
        ClearMapVisuals();
        
        float oX = width/2f - 0.5f, oZ = height/2f - 0.5f;
        for (int x=0; x<width; x++) {
            for (int y=0; y<height; y++) {
                Vector3 pos = new Vector3(x - oX, 0, y - oZ);
                for(int l=0; l<3; l++) {
                    int pid = _grid.GetLayerID(x, y, l);
                    int variant = _grid.GetLayerVariant(x, y, l);
                    if(pid != 0) CreateObj(pid, variant, pos, l, x, y);
                }
            }
        }
        UpdatePlayerVis();
    }

    void CreateObj(int id, int var, Vector3 pos, int layer, int x, int y)
    {
        TileData d = _grid.GetTileDataFromPacked(id);
        if (d != null) {
            VisualVariant v = d.GetVariantByWeight(var);
            if (v.prefab != null) {
                GameObject go = Instantiate(v.prefab, pos, Quaternion.Euler(v.rotation));
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
                foreach (Renderer r in renderers) if (v.overrideMat != null) r.material = v.overrideMat;

                go.transform.parent = transform;
                
                float scaleY = (layer == 0) ? tileHeight : d.heightMultiplier;
                float xzScale = (layer == 0) ? 1.0f : tileSizeXZ;
                go.transform.localScale = new Vector3(xzScale, scaleY, xzScale);
                
                float yOff = (layer == 2) ? tileHeight * 3.0f : 0f;
                go.transform.position += Vector3.up * yOff;

                if (layer == 1) objMap[x, y] = go;

                TileInfo info = go.AddComponent<TileInfo>();
                info.isPush = d.isPush;
            }
        }
    }

    public void SyncCreatedObjects()
    {
        float oX = width/2f - 0.5f, oZ = height/2f - 0.5f;
        for(int x=0; x<width; x++) {
            for(int y=0; y<height; y++) {
                if (_grid.GetLayerID(x,y,1) != 0 && objMap[x,y] == null) {
                    Vector3 pos = new Vector3(x - oX, 0, y - oZ);
                    CreateObj(_grid.GetLayerID(x,y,1), _grid.GetLayerVariant(x,y,1), pos, 1, x, y);
                }
            }
        }
    }

    public void UpdatePlayerVis()
    {
        if (IsAnimating || objPlayer == null) return;
        Vector2Int i = _grid.PlayerIndex;
        objPlayer.transform.position = GetWorldPos(i);
        
        if (objPlayer.GetComponent<PlayerController>() == null) {
            var pc = objPlayer.AddComponent<PlayerController>();
            pc.Init(FindFirstObjectByType<GameManager>());
        }
    }

    public void RotatePlayer(int dx, int dy) {
        if(objPlayer && (dx!=0 || dy!=0)) 
            objPlayer.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dy));
    }

    // --- 애니메이션 루틴 ---

    // ★ [수정] Undo 시 상자도 같이 당겨오는 로직 추가
    // ★ [수정] Undo 이동 (상자 부활 및 이동 통합)
    public IEnumerator AnimateUndoMove(Vector2Int currentVisualPos, int dx, int dy)
    {
        IsAnimating = true;
        
        // 1. 플레이어 이동 준비 (현재 위치 -> 과거 위치)
        Vector3 pStart = GetWorldPos(currentVisualPos); 
        Vector3 pEnd = GetWorldPos(currentVisualPos + new Vector2Int(dx, dy)); 
        
        // 2. 상자 처리 로직
        // Undo 방향(dx,dy)의 반대편에 있는 상자를 당겨와야 함
        Vector2Int visualBoxPos = currentVisualPos - new Vector2Int(dx, dy); // 화면상 상자 위치
        Vector2Int finalBoxPos = currentVisualPos; // 상자가 도착할 위치 (플레이어가 서 있던 곳)
        
        Transform boxT = null;
        Vector3 bStart = Vector3.zero, bEnd = Vector3.zero;

        // A. 화면에 상자가 있는 경우 (일반적인 밀기 취소)
        if (IsBoxAt(visualBoxPos))
        {
            GameObject boxObj = objMap[visualBoxPos.x, visualBoxPos.y];
            TileInfo info = boxObj.GetComponent<TileInfo>();
            
            // 그게 밀 수 있는 상자라면 당겨옴
            if(info != null && info.isPush)
            {
                boxT = boxObj.transform;
                bStart = boxT.position;
                bEnd = pStart; // 플레이어 위치로 당겨짐
                
                // 내부 배열 위치도 미리 이동시켜 둠 (중복 방지)
                objMap[finalBoxPos.x, finalBoxPos.y] = boxObj;
                objMap[visualBoxPos.x, visualBoxPos.y] = null;
            }
        }
        // B. 화면에 상자가 없는 경우 (함정에 빠져서 파괴된 상자 부활)
        else 
        {
            // 데이터상으로는 상자가 있어야 한다면? (RestoreMapData 덕분에 데이터는 있음)
            int restoredId = _grid.GetLayerID(finalBoxPos.x, finalBoxPos.y, 1);
            TileData restoredTile = _grid.GetTileDataFromPacked(restoredId);

            if (restoredTile != null && restoredTile.isPush)
            {
                // 즉시 부활 (애니메이션 없이 뿅 나타남)
                Vector3 spawnPos = pStart; // 플레이어 위치에 생성
                CreateObj(restoredId, _grid.GetLayerVariant(finalBoxPos.x, finalBoxPos.y, 1), spawnPos, 1, finalBoxPos.x, finalBoxPos.y);
                
                // 부활한 상자를 boxT에 연결하면 '당겨오는' 연출도 가능하지만,
                // 함정에서 나오는 건 뿅 생기는 게 자연스러우므로 여기선 생성만 함.
            }
        }

        // 3. Lerp 애니메이션
        float elapsed = 0f;
        while (elapsed < moveDuration) {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            t = t * t * (3f - 2f * t);
            
            if(objPlayer) objPlayer.transform.position = Vector3.Lerp(pStart, pEnd, t);
            if(boxT) boxT.position = Vector3.Lerp(bStart, bEnd, t);
            
            yield return null;
        }
        
        if(objPlayer) objPlayer.transform.position = pEnd;
        if(boxT) boxT.position = bEnd;

        IsAnimating = false;
        RefreshView(); // 최종 정리
    }

    public IEnumerator AnimateMoveRoutine(Vector2Int oldPos, int dx, int dy)
    {
        IsAnimating = true;
        Vector3 pStart = GetWorldPos(oldPos);
        Vector3 pEnd = GetWorldPos(oldPos + new Vector2Int(dx, dy));

        Vector2Int targetPos = oldPos + new Vector2Int(dx, dy);
        Transform boxT = null;
        Vector3 bStart = Vector3.zero, bEnd = Vector3.zero;

        if (IsBoxAt(targetPos)) 
        {
            GameObject targetObj = objMap[targetPos.x, targetPos.y];
            TileInfo info = targetObj.GetComponent<TileInfo>();
            if (info != null && info.isPush)
            {
                boxT = targetObj.transform;
                bStart = boxT.position;
                bEnd = bStart + new Vector3(dx * tileSizeXZ, 0, dy * tileSizeXZ);
            }
        }

        float elapsed = 0f;
        while (elapsed < moveDuration) {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            t = t * t * (3f - 2f * t);
            if (objPlayer) objPlayer.transform.position = Vector3.Lerp(pStart, pEnd, t);
            if (boxT) boxT.position = Vector3.Lerp(bStart, bEnd, t);
            yield return null;
        }
        if (objPlayer) objPlayer.transform.position = pEnd;
        if (boxT) boxT.position = bEnd;

        IsAnimating = false;
        RefreshView();
    }

    public IEnumerator AnimateShiftRoutine(bool isRow, int index, int dir, bool playerMoved)
    {
        IsAnimating = true;
        int visualDir = -dir;
        List<Transform> movingObjs = new List<Transform>();
        int loopCount = isRow ? width : height;

        for (int k = 0; k < loopCount; k++) {
            int x = isRow ? k : index;
            int y = isRow ? index : k;
            
            if (objMap[x, y] != null) {
                int nextK = (k + visualDir + loopCount) % loopCount;
                int nX = isRow ? nextK : index;
                int nY = isRow ? index : nextK;
                
                if (new Vector2Int(nX, nY) == _grid.PlayerIndex && !playerMoved) continue;
                movingObjs.Add(objMap[x, y].transform);
            }
        }

        if (playerMoved && objPlayer) movingObjs.Add(objPlayer.transform);

        GameObject ghostObj = null;
        int wrapK = (visualDir == 1) ? loopCount - 1 : 0;
        int wx = isRow ? wrapK : index;
        int wy = isRow ? index : wrapK;
        
        int gDestK = (visualDir == 1) ? 0 : loopCount - 1;
        int gx = isRow ? gDestK : index;
        int gy = isRow ? index : gDestK;
        bool ghostBlocked = (new Vector2Int(gx, gy) == _grid.PlayerIndex && !playerMoved);

        if (objMap[wx, wy] != null && !ghostBlocked) {
            ghostObj = Instantiate(objMap[wx, wy]);
            float startK = (visualDir == 1) ? -1 : loopCount;
            float sx = isRow ? startK : index;
            float sy = isRow ? index : startK;
            float oX = width/2f - 0.5f, oZ = height/2f - 0.5f;
            ghostObj.transform.position = new Vector3(sx - oX, 0, sy - oZ);
            movingObjs.Add(ghostObj.transform);
        }

        float elapsed = 0f;
        Dictionary<Transform, Vector3> startPos = new Dictionary<Transform, Vector3>();
        Dictionary<Transform, Vector3> endPos = new Dictionary<Transform, Vector3>();
        Vector3 moveVec = isRow ? new Vector3(visualDir * tileSizeXZ, 0, 0) : new Vector3(0, 0, visualDir * tileSizeXZ);

        foreach (var t in movingObjs) {
            startPos[t] = t.position;
            endPos[t] = t.position + moveVec;
        }

        while (elapsed < moveDuration) {
            elapsed += Time.deltaTime;
            float t = elapsed / moveDuration;
            t = t * t * (3f - 2f * t);
            foreach (var tr in movingObjs) if (tr != null) tr.position = Vector3.Lerp(startPos[tr], endPos[tr], t);
            yield return null;
        }

        if (ghostObj) Destroy(ghostObj);
        IsAnimating = false;
        RefreshView();
    }

    void ClearMapVisuals() {
        List<GameObject> toDestroy = new List<GameObject>();
        foreach(Transform child in transform) {
            if (objPlayer != null && child == objPlayer.transform) continue;
            toDestroy.Add(child.gameObject);
        }
        foreach(GameObject go in toDestroy) Destroy(go);
    }

    Vector3 GetWorldPos(Vector2Int index) {
        float oX = width / 2f - 0.5f;
        float oZ = height / 2f - 0.5f;
        return new Vector3(index.x - oX, 0f, index.y - oZ);
    }
    bool IsBoxAt(Vector2Int pos) {
        if (pos.x < 0 || pos.x >= width || pos.y < 0 || pos.y >= height) return false;
        return objMap[pos.x, pos.y] != null;
    }

    void AutoAdjustCamera() { 
        Camera cam = Camera.main;
        if (cam == null) return;
        float mapW = width * tileSizeXZ;
        float mapH = height * tileSizeXZ;
        float padding = 2.0f; 
        if (cam.orthographic) {
            float vertSize = (mapH / 2f) + padding;
            float horzSize = ((mapW / 2f) + padding) / cam.aspect;
            cam.orthographicSize = Mathf.Max(vertSize, horzSize);
            cam.transform.position = -cam.transform.forward * 50f;
        } else {
            float targetSize = Mathf.Max(mapW, mapH) + padding * 2;
            float distance = targetSize / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = Vector3.zero - (cam.transform.forward * distance);
        }
    }
}