using UnityEngine;
using System;
using System.Collections.Generic;

public class GridSystem : MonoBehaviour
{
    private int width;
    private int height;
    
    // 3D Array: [x, y, layer]
    private int[,,] maps; 
    
    private TileData[] tilePalette;
    public Vector2Int PlayerIndex { get; private set; }

    // --- Core Game Events ---
    public Action OnMapChanged;
    public Action OnPlayerMoved;
    public Action OnTrapTriggered;
    public Action OnGoalTriggered;
    
    // --- Audio Events (Sending TileData for specific sounds) ---
    public Action<TileData> OnSoundWalk;    // Walking (sends Floor TileData)
    public Action<TileData> OnSoundPush;    // Pushing (sends Box TileData)
    public Action<TileData> OnSoundDestroy; // Destroying (sends Box TileData)
    
    // These events don't need specific tile data usually, but can stay as simple Actions
    public Action OnSoundShift;   
    public Action OnSoundSuccess; 
    public Action OnSoundFail;    

    private const int PLAYER_LAYER = 1;

    public void Initialize(int w, int h, int[,,] loadedMaps, TileData[] palette, Vector2Int startPos)
    {
        width = w;
        height = h;
        tilePalette = palette;
        PlayerIndex = startPos;
        maps = loadedMaps; 

        // Packing Data
        for (int l = 0; l < 3; l++) {
            for (int x = 0; x < w; x++) {
                for (int y = 0; y < h; y++) {
                    int id = maps[x, y, l];
                    if (id != 0) {
                        int variant = UnityEngine.Random.Range(0, 100);
                        maps[x, y, l] = id | (variant << 16);
                    }
                }
            }
        }
    }

    // --- Data Retrieval ---
    public int GetLayerID(int x, int y, int layer) {
        if (!IsInMap(x, y)) return 0;
        return maps[x, y, layer] & 0xFFFF; 
    }
    
    public int GetLayerVariant(int x, int y, int layer) {
        if (!IsInMap(x, y)) return 0;
        return (maps[x, y, layer] >> 16) & 0xFFFF; 
    }

    private TileData GetTileDataFromPacked(int packedData) {
        if (packedData == 0) return null; // 0 means empty/null in packed data context if not using -1
        int id = packedData & 0xFFFF;
        // Logic to handle -1 as empty if passed raw, though usually masking handles it.
        // Assuming your map data uses -1 for empty in logic but packed might differ.
        // If packedData is -1, it returns null.
        if (id == 65535) return null; // -1 in 16bit unsigned check usually
        
        foreach (var data in tilePalette) if (data.tileID == id) return data;
        return null;
    }

    // --- [1] Player Movement ---
    public bool TryMovePlayer(int dx, int dy)
    {
        int nextX = PlayerIndex.x + dx;
        int nextY = PlayerIndex.y + dy;
        
        if (!IsInMap(nextX, nextY)) return false;

        int layer = 1;
        TileData nextObj = GetTileDataFromPacked(maps[nextX, nextY, layer]);

        // 1. Try Pushing
        if (nextObj != null && nextObj.isPush) {
            int pushX = nextX + dx;
            int pushY = nextY + dy;
            
            if (!IsInMap(pushX, pushY)) return false;

            TileData destObj = GetTileDataFromPacked(maps[pushX, pushY, layer]);
            TileData destFloor = GetTileDataFromPacked(maps[pushX, pushY, 0]);

            // Check if destination is passable (Empty or Not Stop)
            bool isDestPassable = (destObj == null) || (!destObj.isStop);
            bool isFloorPassable = (destFloor == null) || (!destFloor.isStop);

            if (isDestPassable && isFloorPassable) {
                // Check if destination is a Trap (isDead)
                // If destObj is a trap OR destFloor is a trap
                bool isTrap = (destObj != null && destObj.isDead) || (destFloor != null && destFloor.isDead);
                
                // Store the box data for sound event before potentially destroying it
                TileData boxData = nextObj; 

                if (isTrap)
                {
                    // Destroy Box: Clear current position, do not move to new position
                    maps[nextX, nextY, layer] = -1; 
                    OnSoundDestroy?.Invoke(boxData); // Play specific destroy sound
                }
                else
                {
                    // Normal Move
                    maps[pushX, pushY, layer] = maps[nextX, nextY, layer]; 
                    maps[nextX, nextY, layer] = -1; 
                    OnSoundPush?.Invoke(boxData);    // Play specific push sound
                }

                OnMapChanged?.Invoke();
            } else {
                return false; // Cannot push (blocked)
            }
        }
        // 2. Wall Check (If not pushable or push failed)
        else if (IsStopAt(nextX, nextY)) {
            return false;
        }
        // 3. Normal Walk
        else {
             // Get floor data for walking sound
             TileData floorData = GetTileDataFromPacked(maps[nextX, nextY, 0]);
             OnSoundWalk?.Invoke(floorData); // Play specific floor sound
        }

        // 4. Update Player Position
        PlayerIndex = new Vector2Int(nextX, nextY);
        OnPlayerMoved?.Invoke();
        CheckFoot();
        return true;
    }

    private bool IsStopAt(int x, int y) {
        for (int l = 0; l < 3; l++) {
            TileData t = GetTileDataFromPacked(maps[x, y, l]);
            if (t != null && t.isStop) return true;
        }
        return false;
    }

    // --- [2] Map Shifting ---
    public bool TryPushRow(int dir)
    {
        if (!CanPushRow(PlayerIndex.y)) return false;
        
        OnSoundShift?.Invoke(); // Play shift sound
        ShiftRow(PlayerIndex.y, -dir);
        
        if (!ResolveCollision(-dir, true))
        {
            ShiftRow(PlayerIndex.y, dir); // Undo shift if collision fails
            return false;
        }
        return true;
    }

    public bool TryPushCol(int dir)
    {
        if (!CanPushCol(PlayerIndex.x)) return false;
        
        OnSoundShift?.Invoke(); // Play shift sound
        ShiftCol(PlayerIndex.x, -dir);
        
        if (!ResolveCollision(-dir, false))
        {
            ShiftCol(PlayerIndex.x, dir);
            return false;
        }
        return true;
    }

    // --- [3] Collision Resolution ---
    bool ResolveCollision(int dir, bool isRow) {
        if (!HandleLayerCollision(1, dir, isRow)) return false;
        CheckFoot();
        return true;
    }

    bool HandleLayerCollision(int layer, int dir, bool isRow) {
        int packedData = maps[PlayerIndex.x, PlayerIndex.y, layer];
        TileData tile = GetTileDataFromPacked(packedData);

        if (tile == null) return true; 

        bool isBlocked = false;

        // 1. Check Push Property
        if (tile.isPush) {
            if (TryPushChain(PlayerIndex.x, PlayerIndex.y, dir, isRow, layer)) {
                OnMapChanged?.Invoke();
                return true; 
            } else {
                isBlocked = true; 
            }
        }

        // 2. Check Stop or Blocked
        if (tile.isStop || isBlocked) {
            // Push player out
            int newX = PlayerIndex.x + (isRow ? dir : 0);
            int newY = PlayerIndex.y + (isRow ? 0 : dir);
            
            if (IsInMap(newX, newY)) {
                PlayerIndex = new Vector2Int(newX, newY);
                OnPlayerMoved?.Invoke();
                return true;
            } else {
                return false; // Player pushed out of map bounds
            }
        }

        return true;
    }

    // Chain Push Logic (Handles destruction on traps)
    bool TryPushChain(int startX, int startY, int dir, bool isRow, int layer) {
        int loopCount = isRow ? width : height;
        List<Vector2Int> chainCoords = new List<Vector2Int>();
        List<int> chainData = new List<int>();

        int curX = startX;
        int curY = startY;

        bool hitTrap = false; 

        // 1. Analyze the chain
        for (int i = 0; i < loopCount; i++) {
            chainCoords.Add(new Vector2Int(curX, curY));
            chainData.Add(maps[curX, curY, layer]);

            int nextX = curX;
            int nextY = curY;
            if (isRow) nextX = GetWrappedIndex(curX - dir, width);
            else       nextY = GetWrappedIndex(curY - dir, height);

            TileData nextTile = GetTileDataFromPacked(maps[nextX, nextY, layer]);
            TileData floorTile = GetTileDataFromPacked(maps[nextX, nextY, 0]);

            // A. Empty Space
            if (nextTile == null) {
                if (floorTile != null && floorTile.isStop) return false; // Blocked by floor wall/hole
                
                // If floor is trap, mark it
                if (floorTile != null && floorTile.isDead) hitTrap = true;

                chainCoords.Add(new Vector2Int(nextX, nextY));
                break; 
            }
            // B. Object is Trap
            else if (nextTile.isDead) {
                hitTrap = true; 
                chainCoords.Add(new Vector2Int(nextX, nextY));
                break; 
            }
            // C. Pushable Object -> Continue Chain
            else if (nextTile.isPush) {
                curX = nextX;
                curY = nextY;
                continue;
            }
            // D. Wall/Stop -> Blocked
            else if (nextTile.isStop) {
                return false; 
            }
            // E. Passable -> Treat as empty
            else {
                chainCoords.Add(new Vector2Int(nextX, nextY));
                break;
            }
        }

        if (!hitTrap && chainCoords.Count == chainData.Count) return false;

        // 2. Execute Movement (Back to Front)
        for (int i = 0; i < chainData.Count; i++) {
            Vector2Int dest = chainCoords[i + 1];
            
            // If this is the leading box and it hit a trap
            if (hitTrap && i == chainData.Count - 1) {
                int dyingBoxID = chainData[i];
                TileData dyingBox = GetTileDataFromPacked(dyingBoxID);
                
                // Trigger Destroy Sound
                OnSoundDestroy?.Invoke(dyingBox); 
                
                // Do NOT write to destination (Box destroyed)
                // The destination (trap) remains as is.
            }
            else {
                // Normal move
                maps[dest.x, dest.y, layer] = chainData[i];
            }
        }

        // Clear start position
        maps[startX, startY, layer] = -1;

        return true;
    }

    // --- Utilities ---
    void CheckFoot() {
        int myLayerData = maps[PlayerIndex.x, PlayerIndex.y, PLAYER_LAYER];
        CheckLayerEvent(myLayerData);
    }
    void CheckLayerEvent(int packedData) {
        TileData t = GetTileDataFromPacked(packedData);
        if (t != null) {
            if (t.isDead) OnTrapTriggered?.Invoke();
            if (t.isGoal) OnGoalTriggered?.Invoke();
        }
    }
    bool CanPushRow(int y) {
        for (int x = 0; x < width; x++)
            for (int l = 0; l < 3; l++) 
                if (IsFixed(x, y, l)) return false;
        return true;
    }
    bool CanPushCol(int x) {
        for (int y = 0; y < height; y++)
            for (int l = 0; l < 3; l++)
                if (IsFixed(x, y, l)) return false;
        return true;
    }
    bool IsFixed(int x, int y, int l) {
        TileData t = GetTileDataFromPacked(maps[x, y, l]);
        return t != null && !t.isShift;
    }
    void ShiftRow(int y, int dir) { for(int l=0; l<3; l++) ShiftArray(l, y, dir, true); OnMapChanged?.Invoke(); }
    void ShiftCol(int x, int dir) { for(int l=0; l<3; l++) ShiftArray(l, x, dir, false); OnMapChanged?.Invoke(); }
    void ShiftArray(int layer, int targetIndex, int dir, bool isRow) {
        if (isRow) {
            int y = targetIndex;
            if (dir == 1) { int l = maps[width - 1, y, layer]; for (int x = width - 1; x > 0; x--) maps[x, y, layer] = maps[x - 1, y, layer]; maps[0, y, layer] = l; } 
            else { int f = maps[0, y, layer]; for (int x = 0; x < width - 1; x++) maps[x, y, layer] = maps[x + 1, y, layer]; maps[width - 1, y, layer] = f; }
        } else {
            int x = targetIndex;
            if (dir == 1) { int l = maps[x, height - 1, layer]; for (int y = height - 1; y > 0; y--) maps[x, y, layer] = maps[x, y - 1, layer]; maps[x, 0, layer] = l; } 
            else { int f = maps[x, 0, layer]; for (int y = 0; y < height - 1; y++) maps[x, y, layer] = maps[x, y + 1, layer]; maps[x, height - 1, layer] = f; }
        }
    }
    bool IsInMap(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
    int GetWrappedIndex(int index, int max) => (index % max + max) % max;
    public int[,,] GetMapSnapshot() => (int[,,])maps.Clone();
    public void RestoreMapData(int[,,] savedMap, Vector2Int savedPlayerIndex)
    {
        maps = savedMap;
        PlayerIndex = savedPlayerIndex;
        OnMapChanged?.Invoke(); 
        OnPlayerMoved?.Invoke();
    }
}