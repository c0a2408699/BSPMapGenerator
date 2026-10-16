using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;

/// <summary>
/// BSP（Binary Space Partitioning）で部屋と通路を生成し、プレイヤー周辺のセルだけを
/// 必要に応じて配置・破棄するダンジョンマップ生成コンポーネント。
/// 同一シードと同一設定から、再現性のあるチャンク構成を生成する。
///
/// セットアップ:
/// 1. シーン内の管理オブジェクトに本コンポーネントを追加し、Player に追跡対象 Transform を設定する。
/// 2. PlaceCell をシーンに配置する（本クラスが起動時に検索する）。
/// 3. Room / Corridor / Entry / Exit / Event の各 Prefab 設定と、Biome の使用リストを登録する。
///    prefab_type の PrefabLogic には、各セル固有の初期化を行う CellPrefabLogic を設定する。
/// 4. 各プレハブが参照する Mesh を使う場合は、対象モデルの Read/Write Enabled を有効にする。
/// 5. 描画距離・チャンクサイズ・マップ範囲を設定する。Input Seed が 0 の場合は実行時に乱数シードを採用する。
///
/// 操作:
/// - Regenerate(seed): 指定シードで再生成。0 を渡すと新しいランダムシードを使用する。
/// - placementsPerFrame: 1フレームに配置するセル数の上限を調整する。
///
/// 注意:
/// - FloorData、ChunkData、CellData、RoomData、BSPLeafData、PlaceCell、CellPrefabLogic、
///   CellType、RandomSelect、conversion はプロジェクト側の依存型。
/// - Unity のメインスレッドから呼び出すこと。
/// </summary>
public class BSPMapGenerator : MonoBehaviour
{

    //Hash()用途列挙型(purpose引数)
    private enum HashPurpose
    {
        ChunkBiomeSelect = 0,
        RoomDivisionNum = 1, 
        BSPLeafSelect = 2,
        BSPDivisionAxis = 3,
        BSPDivisionPoint = 4,
        RoomTypeSelect = 5,
        LocalRoomStartX = 6,
        LocalRoomStartY = 7,
        RoomSizeX = 8,
        RoomSizeZ = 9,
        DoubleRoomStartX = 10,
        DoubleRoomStartY = 11,
        DoubleRoomSizeX = 12,
        DoubleRoomSizeZ = 13,
        ChunkDisconnectDir1 = 14,
        ChunkDisconnectDir2 = 15,
        ChunkDisconnectDir3 = 22,
        ConnectionPointX = 16,
        ConnectionPointY = 17,
        CorridorPick = 18,
        CorridorCorner = 19, 
        CorridorTypeSelect = 20,
        keikoutouSwitch = 21 , 
        FloorEntrance = 23,
        Floorevent = 24,
        SpecialLeafevent = 25,
        SpecialPrefabevent = 26,
        EntryCell = 30,
        EntryPrefab = 31,
        FloorExit = 32,
        ExitCell = 33,
        ExitPrefab = 34,
    }

    [System.Serializable]
    public class prefab_type
    {
        public CellType cellType;
        public CellPrefabLogic PrefabLogic;
        [Range(0, 100)] public int placementChance = 80;
        public int x_widthmin = 2;
        public int x_widthmax = 3;
        public int z_widthmin = 2;
        public int z_widthmax = 3;
        
        public bool keikoutouSwitchRandom = true;
        public bool placePipes = true;
        public bool sizeLimit = true;
        public bool doubleRoom = false;
    }
    
    [System.Serializable]
    public class chunkBiome_Type
    {
        [Range(0, 100)] public int placementChance = 80;
        public int maxRoomNum = 17;
        public int minRoomNum = 14;
        public List<int> useRoom_Index = null;
        public List<int> useCorridor_Index = null;
    }
    //--------取得するコンポーネント--------
    private PlaceCell placeCell;
    


    //------------入力する変数------------
    [SerializeField] private Transform player;//マップ生成の中心
    [SerializeField] private int placecellRadius = 10;//描画距離
    [SerializeField] private int chunkcellSize = 10;//チャンクのサイズ
    [SerializeField] private int inputSeed = 0;//シード値

    [SerializeField] private List<prefab_type> entryRoomPrefabs = new(); 
    [SerializeField] private List<prefab_type> exitRoomPrefabs = new(); 
    [SerializeField] private List<prefab_type> eventRoomPrefabs = new();  
    [SerializeField] private List<prefab_type> roomPrefabs = new();
    [SerializeField] private List<prefab_type> corridorPrefabs = new();
    [SerializeField] private List<chunkBiome_Type> chunkBiomeTypes = new();

    [SerializeField] private Vector2Int mapChunkCount = new(4, 4); // チャンク数(原点から+方向)

    
    public int placementsPerFrame = 3; // 1フレームでオブジェクトの生成と配置をするセルの数の上限

    //--------その他のフィールド変数----------
    public static BSPMapGenerator Instance { get; private set; }
    public static int cellSize = 16;//セル一つのサイズ
    public static int baseSeed;
    private bool entryPlaced = false;
    public FloorData currentFloor;
    public Vector2Int aChunk;
    public Vector2Int bChunk;
    public Vector2Int validPlayerCell = new Vector2Int(0,0);//プレイヤーが最後にいたnullじゃないcellのキー座標
    private readonly HashSet<Vector2Int> _generatecells = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _removecells = new List<Vector2Int>();
    // List は破棄順を維持し、HashSet は重複確認を O(1) で行う。
    private readonly List<Vector2Int> prefabPlacementCheckedCells = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> placedCellLookup = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, ChunkData> chunkDatas = new Dictionary<Vector2Int, ChunkData>();
    private Dictionary<Vector2Int,CellData> corriderCells = new Dictionary<Vector2Int,CellData>();
    private Vector2Int lastplayerCell = new Vector2Int(int.MinValue, int.MinValue);
    private List<Vector2Int> directionOffset = new()
    {
        new Vector2Int(0, 1),
        new Vector2Int(0,-1),
        new Vector2Int(1, 0),
        new Vector2Int(-1,0)
    };
    private readonly HashSet<Vector2Int> requiredChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> requiredCells = new HashSet<Vector2Int>(); // 常時今必要なセルを保持するhashset
    private readonly Queue<Vector2Int> placementQueue = new Queue<Vector2Int>();  //処理をするcellのリスト
    private readonly HashSet<Vector2Int> queuedCells = new HashSet<Vector2Int>(); //いまplacementQueueにはいいっているセル

    private sealed class CellInitRequest
    {
        public Vector2Int cellKey;
        public CellData cellData;
    }
    

    // 配置済みセルは順序付きリストと高速検索用 HashSet を一緒に更新する。
    private bool IsCellPlacementChecked(Vector2Int cellKey) => placedCellLookup.Contains(cellKey);

    private void MarkCellPlacementChecked(Vector2Int cellKey)
    {
        if (placedCellLookup.Add(cellKey))
            prefabPlacementCheckedCells.Add(cellKey);
    }

    private void UnmarkCellPlacementChecked(Vector2Int cellKey)
    {
        placedCellLookup.Remove(cellKey);
        prefabPlacementCheckedCells.Remove(cellKey);
    }

    private void ClearCellPlacementChecks()
    {
        placedCellLookup.Clear();
        prefabPlacementCheckedCells.Clear();
    }

    #region Unity lifecycle and startup
    void Awake()
    {
        Instance = this;
        placeCell = FindFirstObjectByType<PlaceCell>();
        
        if(inputSeed == 0)
        {
            baseSeed = Random.Range(int.MinValue, int.MaxValue);
            inputSeed = baseSeed;
        }
        else baseSeed = inputSeed;
    }
    private void OnDisable()
    {
        Debug.Log("マップ生成停止");
        deleteAllCell();
        entryPlaced = false; 
    }

    private Coroutine placementCoroutine;
    public bool isRegenerating;
    private bool started;
    void Start() { started = true; StartGeneration(); }
    private void OnEnable() { if (started) StartGeneration(); }

    private async void StartGeneration()
    {
        currentFloor = DecideFloor(1); // 既にStartで呼んでいるなら、ここへ移す

        Vector2Int currentcell = WorldTocell(player.position);
        lastplayerCell = currentcell;
        GenerateCellDatasAndUpdateAsync(currentcell);
        for (int i = 0; i < 6; i++)
        {
            await Task.Yield();
        }
        updateCells(currentcell);

        if (placementCoroutine != null) StopCoroutine(placementCoroutine);
        placementCoroutine = StartCoroutine(cellPlacement()); // 二重起動を防ぐ
    }

    void Update()
    {
        if (isRegenerating) return;
        TryPlaceEntryCellForced();
        Vector2Int currentcell = WorldTocell(player.position);
        if (currentcell != lastplayerCell)
        {
            lastplayerCell = currentcell;
            GenerateCellDatasAndUpdateAsync(currentcell);
            updateCells(currentcell);
            if(getBaseCellData(currentcell) != null)
            {
                validPlayerCell = currentcell;
            }
        }
    }

    #endregion

    #region Coordinate and cell data access
    public Vector2Int WorldTocell(Vector3 pos)
    {
        return new Vector2Int(Mathf.FloorToInt(pos.x / cellSize),Mathf.FloorToInt(pos.z / cellSize));
    }

    public Vector2Int cellToChunk(Vector2Int cell)//チャンク/チャンクサイズ(あまり切り捨て)* チャンクサイズ
    {
        return new Vector2Int(Mathf.FloorToInt((float)cell.x / chunkcellSize) * chunkcellSize,Mathf.FloorToInt((float)cell.y / chunkcellSize) * chunkcellSize);
    }

    public List<Vector2Int> ChunkToCells(Vector2Int chunk)
    {
        List<Vector2Int> cells = new List<Vector2Int>(chunkcellSize * chunkcellSize);

        for (int x = 0; x < chunkcellSize; x++)
        {
            for (int z = 0; z < chunkcellSize; z++)
            {
                cells.Add(new Vector2Int(chunk.x + x, chunk.y + z));
            }
        }

        return cells;
    }
    public CellData getCellData(Vector2Int cellkey)
    {
        var chunkkey = cellToChunk(cellkey);
        return chunkDatas.TryGetValue(chunkkey, out ChunkData chunkData)
            && chunkData.cellDatas.TryGetValue(cellkey, out CellData cellData)
            ? cellData
            : null;
    }

    #endregion

    #region Map data generation and floor setup
    private bool isGenerating = false;
    private void GenerateCellDatasAndUpdateAsync(Vector2Int playerCell)
    {
        if (isGenerating) return; // 生成中なら何もしない(二重起動防止)
        isGenerating = true;

        var initRequests = new List<CellInitRequest>();

        Task.Run(() =>
        {
            try
            {
                GenerateCellDatasOnWorkerThread(playerCell, initRequests);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e); 
            }
            finally
            {
                isGenerating = false;
            }
        });
    }
    private void TryPlaceEntryCellForced()
    {
        if (entryPlaced) return;
        var f = currentFloor;
        if (f == null || !f.entryDecided) return;
        if (placeCell == null) return;
        if (isGenerating) return; // ワーカーがDictionaryを触り終わるまで待つ

        Vector2Int key = f.entryCell;
        ChunkData chunkData = getOrCreatechunkData(cellToChunk(key));
        if (chunkData == null) return;

        // 通常ルートで既に配置が始まっていたら、それで済ませる
        if (IsCellPlacementChecked(key))
        {
            entryPlaced = true;
            return;
        }

        CellData cellData = getBaseCellData(key);
        if (cellData == null || cellData.cellType != CellType.Entry) return;

        // 通常のキャッシュ処理と同じ状態にする
        chunkData.cellDatas[key] = cellData;
        chunkData.generatedCellDatas = true;

        var logic = cellData.prefabType?.PrefabLogic;
        if (logic != null)
        {
            logic.cellData = cellData;
            logic.Init();
        }

        MarkCellPlacementChecked(key); // 破棄管理対象に登録
        StartCoroutine(placeCell.placeCellPrefab(key, cellData));
        entryPlaced = true;
    }

   
    // マップ再生成newSeed が 0 ならランダムなシードにする
    public void Regenerate(int newSeed = 0)
    {
        if (isRegenerating) return;
        StartCoroutine(RegenerateRoutine(newSeed));
    }

    private IEnumerator RegenerateRoutine(int newSeed)
    {
        isRegenerating = true;

        // ワーカースレッドの生成が終わるのを待つ(辞書を触っている最中に消さないため)
        while (isGenerating) yield return null;

        // 配置コルーチンを止めて、配置済みオブジェクトを全部破棄
        if (placementCoroutine != null) StopCoroutine(placementCoroutine);
        deleteAllCell();

        // 生成データを全てリセット
        chunkDatas.Clear();
        corriderCells.Clear();
        processedConnections.Clear();
        ClearCellPlacementChecks();
        placementQueue.Clear();
        queuedCells.Clear();
        requiredChunks.Clear();
        requiredCells.Clear();
        _generatecells.Clear();
        _removecells.Clear();
        currentFloor = null;
        entryPlaced = false;
        lastplayerCell = new Vector2Int(int.MinValue, int.MinValue);
        validPlayerCell = Vector2Int.zero;

        // シードを更新
        inputSeed = newSeed;
        baseSeed = (newSeed != 0) ? newSeed : Random.Range(int.MinValue, int.MaxValue);
        if (newSeed == 0) inputSeed = baseSeed; // Awakeと同じ挙動

        yield return null; // 破棄(Destroy)が反映されるのを1フレーム待つ

        isRegenerating = false;
        StartGeneration();
    }

    //-------------------------------------------------------------
    // マップのデータ生成（UnityAPI関係の処理に触れないデータ生成専用のワーカースレッド処理）
    //-------------------------------------------------------------
    private void GenerateCellDatasOnWorkerThread(Vector2Int playerCell,List<CellInitRequest> initRequests)
    {
        requiredChunks.Clear();
        for (int dx = -placecellRadius; dx <= placecellRadius; dx++)
        {
            for (int dz = -placecellRadius; dz <= placecellRadius; dz++)
            {
                Vector2Int thiscellkey = new Vector2Int(playerCell.x + dx, playerCell.y + dz);
                requiredChunks.Add(cellToChunk(thiscellkey));
            }
        }
       
        foreach (Vector2Int chunkKey in requiredChunks)
        {
            GenerateChunkConnections(chunkKey);
        }

        DecideEntryCell();
        DecideExitCell();
        foreach (Vector2Int chunkKey in requiredChunks)
        {
            ChunkData chunkData = getOrCreatechunkData(chunkKey);
            if(chunkData == null) continue;

            foreach (Vector2Int cellKey in ChunkToCells(chunkKey))//生成をリクエストされたチャンクのすべてのセル
            {
                CellData cellData = getBaseCellData(cellKey);
                if (cellData == null) continue;
                if (chunkDatas[chunkKey].cellDatas.ContainsKey(cellKey)) continue;
                chunkDatas[chunkKey].cellDatas[cellKey] = cellData;

                if (cellData.prefabType != null && cellData.prefabType.PrefabLogic != null)
                {
                    initRequests.Add(new CellInitRequest
                    {
                        cellKey = cellKey,
                        cellData = cellData
                    });
                }
            }

            chunkData.generatedCellDatas = true;
        }
        foreach (CellInitRequest request in initRequests)
        {
            CellPrefabLogic cellPrefabLogic = request.cellData.prefabType.PrefabLogic;
            if (cellPrefabLogic == null)
                continue;
            cellPrefabLogic.cellData = request.cellData;
            cellPrefabLogic.Init();
        }
    }
    private bool IsChunkAllowed(Vector2Int chunk)
    {
        // chunkキーはchunkcellSizeの倍数なので、インデックスに直して判定
        int cx = chunk.x / chunkcellSize;
        int cy = chunk.y / chunkcellSize;
        return cx >= 0 && cx < mapChunkCount.x && cy >= 0 && cy < mapChunkCount.y;
    }
    FloorData DecideFloor(int floor)
    {
        int w = mapChunkCount.x, h = mapChunkCount.y;
        int total = w * h;

        if (total <= 0) return null;

        // 入口
        int ei = (int)((uint)Hash(
            floor, 0, 0, (int)HashPurpose.FloorEntrance
        ) % (uint)total);

        // イベント
        int xi = ei;

        if (total > 1)
        {
            xi = (int)((uint)Hash(
                floor, 1, 0, (int)HashPurpose.Floorevent
            ) % (uint)(total - 1));

            if (xi >= ei)
                xi++;
        }

        // 出口
        int exitIndex = ei;

        if (total > 1)
        {
            exitIndex = (int)((uint)Hash(
                floor, 2, 0, (int)HashPurpose.FloorExit
            ) % (uint)(total - 1));

            // entrance を飛ばす
            if (exitIndex >= ei)
                exitIndex++;
        }

        Vector2Int entranceIdx = new Vector2Int(
            ei % w,
            ei / w
        );

        Vector2Int eventIdx = new Vector2Int(
            xi % w,
            xi / w
        );

        Vector2Int exitIdx = new Vector2Int(
            exitIndex % w,
            exitIndex / w
        );

        return new FloorData
        {
            floor = floor,
            entranceChunk = entranceIdx * chunkcellSize,
            eventChunk = eventIdx * chunkcellSize,
            exitChunk = exitIdx * chunkcellSize
        };
        #endregion
    }

    #region object place
    //-------------------------------------------------------------
    // 生成したデータに基づいてオブジェクトを生成、配置
    //-------------------------------------------------------------
    void updateCells(Vector2Int playerCell)
    {
        requiredCells.Clear();
        for (int dx = -placecellRadius; dx <= placecellRadius; dx++)
        {
            for (int dz = -placecellRadius; dz <= placecellRadius; dz++)
            {
                Vector2Int cellKey = new Vector2Int(playerCell.x + dx, playerCell.y + dz);
                _generatecells.Add(cellKey);
                requiredCells.Add(cellKey);

                Vector2Int chunkKey = cellToChunk(cellKey);
                chunkDatas.TryGetValue(chunkKey, out ChunkData chunkData);
                CellData cellData = getCellData(cellKey);

                TryPlaceCellPrefab(cellKey, chunkKey, chunkData, cellData);
                //TryplaceKeikoutouControlPanel(cellKey, chunkKey, chunkData, cellData);
            }
        }
        DeleteOutOfRangeCells();
    }


    //プレハブ配置
    void TryPlaceCellPrefab(Vector2Int cellKey, Vector2Int chunkKey, ChunkData chunkData, CellData cellData)
    {
        if (IsCellPlacementChecked(cellKey)) return; // 既に生成済み
        if (queuedCells.Contains(cellKey)) return;                 // 既にキュー待ち
        if (cellData == null || !cellData.generatedData) return;
        if (placeCell == null) return;
        if (chunkData?.cellDatas == null) return;
        if (!chunkData.cellDatas.ContainsKey(cellKey)) return;

        queuedCells.Add(cellKey);
        placementQueue.Enqueue(cellKey);
    }


    //範囲外セルの削除
    void DeleteOutOfRangeCells()
    {
        
        foreach (var cellKey in prefabPlacementCheckedCells)
        {
            if (entryPlaced && currentFloor != null && cellKey == currentFloor.entryCell)
                continue; // 入口セルは常に残す

            if (!_generatecells.Contains(cellKey))
                _removecells.Add(cellKey);
        }
        foreach (var cellKey in _removecells)
        {
            placeCell.DestroyCell(cellKey);
            UnmarkCellPlacementChecked(cellKey);
        }
        _removecells.Clear();
        _generatecells.Clear();
    }
    //すべてのセルの削除
    public void deleteAllCell()
    {
        foreach (var cellKey in prefabPlacementCheckedCells)
        {
            placeCell.DestroyCell(cellKey);
        }
         ClearCellPlacementChecks();
    }
    private IEnumerator cellPlacement()
    {
        while (true)
        {
            int processed = 0;
            int frameLimit = Mathf.Max(1, placementsPerFrame);
            while (processed < frameLimit && placementQueue.Count > 0)
            {
                Vector2Int cellKey = placementQueue.Dequeue();
                queuedCells.Remove(cellKey);

                if (IsCellPlacementChecked(cellKey)) continue;

                //範囲外なら何もせずスキップ
                if (!requiredCells.Contains(cellKey))
                {
                    continue;
                }

                Vector2Int chunkKey = cellToChunk(cellKey);
                if (placeCell == null
                    || !chunkDatas.TryGetValue(chunkKey, out ChunkData chunkData)
                    || chunkData?.cellDatas == null
                    || !chunkData.cellDatas.TryGetValue(cellKey, out CellData cellData)
                    || cellData == null)
                {
                    continue;
                }

                bool alreadyPlaced = placeCell.activeCellDatas != null
                    && placeCell.activeCellDatas.TryGetValue(cellKey, out var activeCellData)
                    && activeCellData?.mainObj != null;
                if (alreadyPlaced) continue;

                // 実際に生成が確定した時点で初めて「配置済み管理リスト」に入れる
                MarkCellPlacementChecked(cellKey);
                StartCoroutine(placeCell.placeCellPrefab(cellKey, cellData));

                processed++;
            }
            yield return null; // 1フレーム待つ
        }
    }
    
    

    //-------------
    //基礎データ生成
    //-------------
    void DecideEntryCell()
    {
        if (currentFloor == null || currentFloor.entryDecided) return;
        if (entryRoomPrefabs.Count == 0) return;

        // 入口チャンクとその4辺の通路を先に作っておく
        var cd = getOrCreatechunkData(currentFloor.entranceChunk);
        if (cd == null) return;
        GenerateChunkConnections(currentFloor.entranceChunk);

        var candidates = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>();

        foreach (var room in cd.RoomList)
        {
            if (room.iseventRoom) continue;

            for (int x = room.roomStartcell.x; x <= room.roomEndcell.x; x++)
            for (int y = room.roomStartcell.y; y <= room.roomEndcell.y; y++)
            {
                foreach (var dir in directionOffset)
                {
                    var n = new Vector2Int(x + dir.x, y + dir.y);
                    if (!seen.Add(n)) continue;
                    if (IsBlockedForEntry(n)) continue;
                    candidates.Add(n);
                }
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("入口セルの候補がありません");
            return;
        }

        int ci = (int)((uint)Hash(currentFloor.floor, 3, 0, (int)HashPurpose.EntryCell) % (uint)candidates.Count);
        int pi = RandomSelect.GetRandomIndex(entryRoomPrefabs, b => b.placementChance,
                    Hash(currentFloor.floor, 4, 0, (int)HashPurpose.EntryPrefab));

        currentFloor.entryCell = candidates[ci];
        currentFloor.entryPrefab = entryRoomPrefabs[pi];
        currentFloor.entryDecided = true; // 最後にtrueにする
    }
    void DecideExitCell()
    {
        if (currentFloor == null || currentFloor.exitDecided) return;
        if (exitRoomPrefabs.Count == 0) return;

        var cd = getOrCreatechunkData(currentFloor.exitChunk);
        if (cd == null) return;
        GenerateChunkConnections(currentFloor.exitChunk); // 通路を先に確定させる

        var candidates = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>();

        foreach (var room in cd.RoomList)
        {
            if (room.iseventRoom) continue;

            for (int x = room.roomStartcell.x; x <= room.roomEndcell.x; x++)
            for (int y = room.roomStartcell.y; y <= room.roomEndcell.y; y++)
            {
                foreach (var dir in directionOffset)
                {
                    var n = new Vector2Int(x + dir.x, y + dir.y);
                    if (!seen.Add(n)) continue;

                    // 出口チャンク内のセルに限定
                    if (cellToChunk(n) != currentFloor.exitChunk) continue;
                    // 入口セルと重ならない
                    if (currentFloor.entryDecided && n == currentFloor.entryCell) continue;
                    if (IsBlockedForEntry(n)) continue;

                    candidates.Add(n);
                }
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("出口セルの候補がありません");
            return;
        }

        int ci = (int)((uint)Hash(currentFloor.floor, 5, 0, (int)HashPurpose.ExitCell) % (uint)candidates.Count);
        int pi = RandomSelect.GetRandomIndex(exitRoomPrefabs, b => b.placementChance,
                    Hash(currentFloor.floor, 6, 0, (int)HashPurpose.ExitPrefab));

        currentFloor.exitCell = candidates[ci];
        currentFloor.exitPrefab = exitRoomPrefabs[pi];
        currentFloor.exitDecided = true; // 最後にtrue
    }

    // 範囲外・部屋・通路のセルは候補にしない
    bool IsBlockedForEntry(Vector2Int cell)
    {
        var chunkData = getOrCreatechunkData(cellToChunk(cell));
        if (chunkData == null) return true; // マップ範囲外

        foreach (var r in chunkData.RoomList)
        {
            if (cell.x >= r.roomStartcell.x && cell.x <= r.roomEndcell.x
                && cell.y >= r.roomStartcell.y && cell.y <= r.roomEndcell.y)
                return true;
        }
        return corriderCells.ContainsKey(cell);
    }

    #endregion

    #region Chunk, room, and corridor generation
    private HashSet<(Vector2Int, Vector2Int)> processedConnections = new HashSet<(Vector2Int, Vector2Int)>();
    private void GenerateChunkConnections(Vector2Int chunk)
    {
        // 自分自身が範囲外なら何もしない
        if (getOrCreatechunkData(chunk) == null) return;

        foreach (var dir in directionOffset)
        {
            Vector2Int targetchunkKey = new Vector2Int(
                chunk.x + (dir.x * chunkcellSize),
                chunk.y + (dir.y * chunkcellSize));

            // 接続先が範囲外(null)なら、このエッジは作らない
            if (getOrCreatechunkData(targetchunkKey) == null) continue;

            Vector2Int p1 = chunk;
            Vector2Int p2 = targetchunkKey;
            Vector2Int dirFromP1 = dir; // p1→p2 方向

            if (p1.x > p2.x || (p1.x == p2.x && p1.y > p2.y))
            {
                (p1, p2) = (p2, p1);
                dirFromP1 = -dir; // swap したので方向も反転
            }

            // 既にこのエッジ(p1-p2間)を処理済みならスキップ
            if (!processedConnections.Add((p1, p2)))
                continue;

            Vector2Int dirFromP2 = -dirFromP1;

            var p1Data = getOrCreatechunkData(p1);
            var p2Data = getOrCreatechunkData(p2);

            if (!p1Data.chunkConnectDirs[dirFromP1]
                && !p2Data.chunkConnectDirs[dirFromP2])
            {
                continue;
            }

            Vector2Int p1ConnPoint = GetNearestConnectionPointToBorder(p1Data, p1, dirFromP1);
            Vector2Int p2ConnPoint = GetNearestConnectionPointToBorder(p2Data, p2, dirFromP2);

            createLjiLineCorridor(p1ConnPoint, p2ConnPoint, p1Data, p1);
        }
    }


    Vector2Int GetNearestConnectionPointToBorder(ChunkData cd, Vector2Int chunk, Vector2Int dir)
    {
        if (cd.roomConnectionPoints.Count == 0)
            return cd.connectionPoint; // フォールバック

        // dir方向の境界に最も近い部屋接続点を選ぶ
        Vector2Int borderTarget = new Vector2Int(
            dir.x != 0 ? (dir.x > 0 ? chunk.x + chunkcellSize - 1 : chunk.x) : (chunk.x + chunkcellSize / 2),
            dir.y != 0 ? (dir.y > 0 ? chunk.y + chunkcellSize - 1 : chunk.y) : (chunk.y + chunkcellSize / 2)
        );

        Vector2Int best = cd.roomConnectionPoints[0];
        int bestDist = int.MaxValue;
        foreach (var p in cd.roomConnectionPoints)
        {
            int d = Mathf.Abs(p.x - borderTarget.x) + Mathf.Abs(p.y - borderTarget.y);
            if (d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }


     //-------------
    //基礎データ生成
    //-------------
    public CellData getBaseCellData(Vector2Int cell)
    {
        Vector2Int chunk = cellToChunk(cell);
        
        ChunkData targetchunkdata = getOrCreatechunkData(chunk);
        if( targetchunkdata == null)  return null;
        

        //そのチャンクに部屋がああるかどうか判定して返す
        int _roomId = 0;
        RoomData hitRoom = null;
        int hitRoomId = 0;


        foreach (var room in targetchunkdata.RoomList)
        {
            _roomId += 1;

            if (cell.x >= room.roomStartcell.x && cell.x <= room.roomEndcell.x
                && cell.y >= room.roomStartcell.y && cell.y <= room.roomEndcell.y)
            {
                if (hitRoom == null)
                {
                    hitRoom = room;
                    hitRoomId = _roomId;
                }

                // 出口部屋が見つかったら、それを最優先にして確定
                if (room.iseventRoom)
                {
                    hitRoom = room;
                    hitRoomId = _roomId;
                    break;
                }
            }
        }
        if (hitRoom != null)
        {
            return new CellData
            {
                thiscellKey = cell,
                chunk = targetchunkdata,
                room = hitRoom,
                prefabType = hitRoom.pt,
                cellId = targetchunkdata.chunkId + hitRoomId,
                cellType = CellType.Room,
            };
        }
        if (currentFloor != null && currentFloor.entryDecided && cell == currentFloor.entryCell)
        {
            return new CellData
            {
                thiscellKey = cell,
                chunk = targetchunkdata,
                cellId = targetchunkdata.chunkId,
                cellType = CellType.Entry,
                prefabType = currentFloor.entryPrefab
            };
        }
        if (currentFloor != null && currentFloor.exitDecided && cell == currentFloor.exitCell)
        {
            return new CellData
            {
                thiscellKey = cell,
                chunk = targetchunkdata,
                cellId = targetchunkdata.chunkId,
                cellType = CellType.Exit,
                prefabType = currentFloor.exitPrefab
            };
        }

        if (corriderCells.ContainsKey(cell))
        {
            CellData _cellData = corriderCells[cell];
       
                return new CellData
                {
                    thiscellKey = cell,
                    chunk = targetchunkdata,
                    cellId = targetchunkdata.chunkId,
                    cellType = CellType.Corridor,
                    prefabType = _cellData.prefabType,
                    connectDir = _cellData.connectDir
                };
            
        }
        return null;
    }

    public ChunkData getOrCreatechunkData(Vector2Int chunk)
    {
        if (!IsChunkAllowed(chunk)) return null;
        if (chunkDatas.ContainsKey(chunk)) return chunkDatas[chunk];
        
        //使用するチャンクのバイオームタイプを決定
        int chunkBiome_Typeseed = Hash(chunk.x, chunk.y, 343, (int)HashPurpose.ChunkBiomeSelect);
        chunkBiome_Type targetchunkBiome = chunkBiomeTypes[RandomSelect.GetRandomIndex(chunkBiomeTypes, b => b.placementChance, chunkBiome_Typeseed)];

        //chunkDataを準備
        ChunkData newChunkData = new ChunkData
        {
            chunkId = (chunk.x*chunk.x+1)*1000,
            chunkBiome = targetchunkBiome,
            cellDatas = new Dictionary<Vector2Int, CellData>(),
            chunkConnectDirs = new Dictionary<Vector2Int, bool>
            {
                { new Vector2Int( 0,  1), true },
                { new Vector2Int( 0, -1), true },
                { new Vector2Int(-1,  0), true },
                { new Vector2Int( 1,  0), true },
            }
        };
        List<BSPLeafData> BSPLeafs = new List<BSPLeafData>();

        BSPLeafData newLeafRoom = new BSPLeafData();
        newLeafRoom.leafStartcell = new Vector2Int(chunk.x,chunk.y);
        newLeafRoom.leafEndcell = new Vector2Int(chunk.x+chunkcellSize-2,chunk.y+chunkcellSize-2);
        BSPLeafs.Add(newLeafRoom);

        //chunkDataを分割
        int RoomDivisionNum = Mathf.Abs(Hash(chunk.x, chunk.y, 998, (int)HashPurpose.RoomDivisionNum)) % (targetchunkBiome.maxRoomNum - targetchunkBiome.minRoomNum + 1) + targetchunkBiome.minRoomNum;
        for(int i = 0; i < RoomDivisionNum; i++)
        {
            int index = Mathf.Abs(Hash(chunk.x, chunk.y, i, (int)HashPurpose.BSPLeafSelect + i * 10)) % BSPLeafs.Count;
            BSPLeafData targetBSPLeafData = BSPLeafs[index];
            BSPLeafs.RemoveAt(index);

            int divisionX = Mathf.Abs(Hash(chunk.x, chunk.y, i, (int)HashPurpose.BSPDivisionAxis + i * 10)) % 2;
            
            bool canDivisionX = targetBSPLeafData.leafEndcell.x - targetBSPLeafData.leafStartcell.x - 1 >= 4;//要修正？
            bool canDivisionZ = targetBSPLeafData.leafEndcell.y - targetBSPLeafData.leafStartcell.y - 1 >= 4;
            if (!canDivisionX && !canDivisionZ)
            {
                BSPLeafs.Add(targetBSPLeafData);
                continue;
            }

            BSPLeafData newLeafRoom1 = new BSPLeafData();
            BSPLeafData newLeafRoom2 = new BSPLeafData();
            
            if (!canDivisionX)divisionX = 0;
            else if (!canDivisionZ)divisionX = 1;

            if (divisionX==1)
            {
                int xflag = Mathf.Abs(Hash(chunk.x, chunk.y, i, (int)HashPurpose.BSPDivisionPoint + i * 10)) % (targetBSPLeafData.leafEndcell.x - targetBSPLeafData.leafStartcell.x - 3) + targetBSPLeafData.leafStartcell.x + 2;
                newLeafRoom1.leafStartcell = new Vector2Int(targetBSPLeafData.leafStartcell.x,targetBSPLeafData.leafStartcell.y);
                newLeafRoom1.leafEndcell = new Vector2Int(xflag - 1,targetBSPLeafData.leafEndcell.y);
                newLeafRoom2.leafStartcell = new Vector2Int(xflag + 1,targetBSPLeafData.leafStartcell.y);
                newLeafRoom2.leafEndcell = new Vector2Int(targetBSPLeafData.leafEndcell.x,targetBSPLeafData.leafEndcell.y);
            }
            else
            {
                int zflag = Mathf.Abs(Hash(chunk.x, chunk.y, i, (int)HashPurpose.BSPDivisionPoint + i * 10)) % (targetBSPLeafData.leafEndcell.y - targetBSPLeafData.leafStartcell.y - 3) + targetBSPLeafData.leafStartcell.y + 2;
                newLeafRoom1.leafStartcell  = new Vector2Int(targetBSPLeafData.leafStartcell.x,targetBSPLeafData.leafStartcell.y);
                newLeafRoom1.leafEndcell = new Vector2Int(targetBSPLeafData.leafEndcell.x, zflag - 1);
                newLeafRoom2.leafStartcell = new Vector2Int(targetBSPLeafData.leafStartcell.x, zflag + 1);
                newLeafRoom2.leafEndcell = new Vector2Int(targetBSPLeafData.leafEndcell.x,targetBSPLeafData.leafEndcell.y);
            }
            targetBSPLeafData.left = newLeafRoom1;
            targetBSPLeafData.right = newLeafRoom2;
            newLeafRoom1.parent = targetBSPLeafData;
            newLeafRoom2.parent = targetBSPLeafData;
            BSPLeafs.Add(newLeafRoom1);
            BSPLeafs.Add(newLeafRoom2);
        }
        
        
        //分割した葉ノードそれぞれに部屋を作る
        int index2 = 0;
        var rooms = new List<RoomData>();
        // 出口チャンクのときだけ、出口にする葉の番号を決める(それ以外は-1)
        int eventLeafIndex = -1;
        if (currentFloor != null && chunk == currentFloor.eventChunk && eventRoomPrefabs.Count > 0)
        {
            eventLeafIndex = (int)((uint)Hash(chunk.x, chunk.y, 0, (int)HashPurpose.SpecialLeafevent) % (uint)BSPLeafs.Count);
        }
        int leafIndex = 0;
        foreach(var leafRoom in BSPLeafs)
        {
            Vector2Int localroomStartcell = Vector2Int.zero;
            int roomSeedId = Hash(chunk.x, chunk.y, index2, (int)HashPurpose.RoomTypeSelect);
            bool iseventLeaf = (leafIndex == eventLeafIndex);
            leafIndex++; // continueより前に必ず進める

            prefab_type pt;
            if (iseventLeaf)
            {
                int eventSeed = Hash(chunk.x, chunk.y, 0, (int)HashPurpose.SpecialPrefabevent);
                pt = eventRoomPrefabs[RandomSelect.GetRandomIndex(eventRoomPrefabs, b => b.placementChance, eventSeed)];
            }
            else
            {
                pt = roomPrefabs[RandomSelect.GetRandomIndex(roomPrefabs, b => b.placementChance, roomSeedId, targetchunkBiome.useRoom_Index)];
            }
            int roomId = conversion.packInt((int)chunk.x+(int)chunk.y,roomSeedId,index2);
            index2++;
            if (pt == null) continue;
            
            localroomStartcell.x = Mathf.Abs(Hash(chunk.x, chunk.y, index2, (int)HashPurpose.LocalRoomStartX + index2 * 10)) % (leafRoom.leafEndcell.x - leafRoom.leafStartcell.x);
            localroomStartcell.y = Mathf.Abs(Hash(chunk.x, chunk.y, index2, (int)HashPurpose.LocalRoomStartY + index2 * 10)) % (leafRoom.leafEndcell.y - leafRoom.leafStartcell.y);//Chunkcellのサイズとwidthmaxが同じだとエラー
            int roomSizeX  = Mathf.Abs(Hash(chunk.x, chunk.y, index2, (int)HashPurpose.RoomSizeX + index2 * 10)) % (pt.x_widthmax - pt.x_widthmin + 1) + pt.x_widthmin;
            int roomSizeZ  = Mathf.Abs(Hash(chunk.x, chunk.y, index2, (int)HashPurpose.RoomSizeZ + index2 * 10)) % (pt.z_widthmax - pt.z_widthmin + 1) + pt.z_widthmin;
            int roomEndX = leafRoom.leafStartcell.x + localroomStartcell.x + roomSizeX;
            int roomEndZ = leafRoom.leafStartcell.y + localroomStartcell.y + roomSizeZ;
            if(roomEndX > leafRoom.leafEndcell.x && pt.sizeLimit) roomEndX = leafRoom.leafEndcell.x;
            if(roomEndZ > leafRoom.leafEndcell.y && pt.sizeLimit) roomEndZ = leafRoom.leafEndcell.y;
            if (roomEndX > chunk.x + chunkcellSize - 1) roomEndX = chunk.x + chunkcellSize - 1;
            if (roomEndZ > chunk.y + chunkcellSize - 1) roomEndZ = chunk.y + chunkcellSize - 1;
            List<Vector2Int> placeKeikoutouControlPanelKey = new();
            for(int i = 0; i<6;i++)
            {
                placeKeikoutouControlPanelKey.Add(RandomSelect.getRandomVector2(new Vector2Int(leafRoom.leafStartcell.x + localroomStartcell.x,leafRoom.leafStartcell.y + localroomStartcell.y),new Vector2Int(roomEndX, roomEndZ),Mathf.Abs(Hash(chunk.x, chunk.y, index2, (int)HashPurpose.RoomSizeZ + index2+i * 10))));
            }
            
            RoomData _room = new RoomData
            {
                roomId = roomId,
                iseventRoom = iseventLeaf,
                roomStartcell = new Vector2Int(leafRoom.leafStartcell.x + localroomStartcell.x,leafRoom.leafStartcell.y + localroomStartcell.y),
                roomEndcell = new Vector2Int(roomEndX, roomEndZ),
                keikoutouSwitchPlaceableKey = placeKeikoutouControlPanelKey,
                 _sizeLimit = pt.sizeLimit,
                pt = pt
            };
            rooms.Add(_room);
            leafRoom.room = _room;

            if(!pt.doubleRoom) continue;
            Vector2Int localroomStartcell2 = Vector2Int.zero;

            index2++;
            int roomSizeX2 = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomSizeX + index2 * 10)) % (pt.x_widthmax - pt.x_widthmin + 1) + pt.x_widthmin;
            int roomSizeZ2 = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomSizeZ + index2 * 10)) % (pt.z_widthmax - pt.z_widthmin + 1) + pt.z_widthmin;
            localroomStartcell2.x = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomStartX + index2 * 10)) % (roomSizeX - pt.x_widthmin + 1);
            localroomStartcell2.y = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomStartY + index2 * 10)) % (roomSizeZ - pt.z_widthmin + 1);
            roomEndX = leafRoom.leafStartcell.x + localroomStartcell.x + localroomStartcell2.x + roomSizeX2;
            roomEndZ = leafRoom.leafStartcell.y + localroomStartcell.y + localroomStartcell2.y + roomSizeZ2;
            if(roomEndX > leafRoom.leafEndcell.x && pt.sizeLimit) roomEndX = leafRoom.leafEndcell.x;
            if(roomEndZ > leafRoom.leafEndcell.y && pt.sizeLimit) roomEndZ = leafRoom.leafEndcell.y;
            if (roomEndX > chunk.x + chunkcellSize - 1) roomEndX = chunk.x + chunkcellSize - 1;
            if (roomEndZ > chunk.y + chunkcellSize - 1) roomEndZ = chunk.y + chunkcellSize - 1;
            RoomData __room = new RoomData
            {
                roomId = roomId,
                roomStartcell = new Vector2Int(
                    leafRoom.leafStartcell.x + localroomStartcell.x + localroomStartcell2.x,
                    leafRoom.leafStartcell.y + localroomStartcell.y + localroomStartcell2.y),
                roomEndcell = new Vector2Int(roomEndX, roomEndZ),
                keikoutouSwitchPlaceableKey = placeKeikoutouControlPanelKey,
                _sizeLimit = pt.sizeLimit,
                pt = pt
            };
            rooms.Add(__room);
        }
        newChunkData.RoomList = rooms;
        

        //通路生成、全葉の親であるnewLeafRoomからすべての子へ通路を生成する
        GenerateCorridors(newLeafRoom, newChunkData, chunk);
        //ソースデータ同士で通路を作らない方向を決める
        if((chunk.x % 2 == 0) && (chunk.y % 2 == 0))
        {
            // 変更: directionOffsetを毎回コピーしていたListを削除し、directionOffsetを直接参照する
            // (中身は使われず.Countしか参照していなかったため、コピー自体が不要だった)
            int first = Mathf.Abs(Hash(chunk.x, chunk.y, 0, (int)HashPurpose.ChunkDisconnectDir1)) % directionOffset.Count;
            int second = Mathf.Abs(Hash(chunk.x, chunk.y, 0, (int)HashPurpose.ChunkDisconnectDir2)) % directionOffset.Count;
            int third = Mathf.Abs(Hash(chunk.x, chunk.y, 0, (int)HashPurpose.ChunkDisconnectDir3)) % directionOffset.Count;

            newChunkData.chunkConnectDirs[directionOffset[first]] = false;
            newChunkData.chunkConnectDirs[directionOffset[second]] = false;
            newChunkData.chunkConnectDirs[directionOffset[third]] = false;
        }
        chunkDatas[chunk] = newChunkData;
        return newChunkData;
    }

    //----------------------------------------------------------
    //すべての葉ノードに接続点を作り、それらのペアをつなぐ関数を呼び出す
    //----------------------------------------------------------
    Vector2Int GenerateCorridors(BSPLeafData node, ChunkData cd, Vector2Int chunk)
    {

        //部屋ごとに接続点を設定する
        if(node.IsLeaf)
        {
            Vector2Int _connectionPoint = node.connectionPoint = new Vector2Int(
                    (chunk.x + chunk.x+chunkcellSize-1) / 2,
                    (chunk.y + chunk.y+chunkcellSize-1) / 2
                );
            if (node.room != null)
            {
                if(node.room._sizeLimit)
                {
                    _connectionPoint = new Vector2Int(
                        Mathf.Abs(Hash(chunk.x, chunk.y, node.leafStartcell.x + node.leafStartcell.y, (int)HashPurpose.ConnectionPointX)) % (node.room.roomEndcell.x - node.room.roomStartcell.x + 1) + node.room.roomStartcell.x,
                        Mathf.Abs(Hash(chunk.x, chunk.y, node.leafStartcell.x + node.leafStartcell.y, (int)HashPurpose.ConnectionPointY)) % (node.room.roomEndcell.y - node.room.roomStartcell.y + 1) + node.room.roomStartcell.y
                    );
                }
                else
                {
                    int roomEndX = node.room.roomEndcell.x;
                    int roomEndZ = node.room.roomEndcell.y;
                    if( node.room.roomEndcell.x > node.leafEndcell.x) roomEndX = node.leafEndcell.x;
                    if( node.room.roomEndcell.y > node.leafEndcell.y) roomEndZ = node.leafEndcell.y;
                    _connectionPoint = new Vector2Int(
                        Mathf.Abs(Hash(chunk.x, chunk.y, node.leafStartcell.x + node.leafStartcell.y, (int)HashPurpose.ConnectionPointX)) % (roomEndX - node.room.roomStartcell.x + 1) + node.room.roomStartcell.x,
                        Mathf.Abs(Hash(chunk.x, chunk.y, node.leafStartcell.x + node.leafStartcell.y, (int)HashPurpose.ConnectionPointY)) % (roomEndZ - node.room.roomStartcell.y + 1) + node.room.roomStartcell.y
                    );
                }
            }
            node.connectionPoint = _connectionPoint;
            cd.roomConnectionPoints.Add(_connectionPoint);
            cd.connectionPoint = _connectionPoint;
            return node.connectionPoint;
        }

       
        Vector2Int leftPoint  = GenerateCorridors(node.left,  cd, chunk);
        Vector2Int rightPoint = GenerateCorridors(node.right, cd, chunk);
        Vector2Int interLeftRightChunkKey = new Vector2Int(leftPoint.x + rightPoint.x,leftPoint.y + rightPoint.y);
        createLjiLineCorridor(leftPoint, rightPoint, cd, chunk);

        int pick = Mathf.Abs(Hash(chunk.x, chunk.y, leftPoint.x + rightPoint.y, (int)HashPurpose.CorridorPick)) % 2;
        node.connectionPoint = (pick == 0) ? leftPoint : rightPoint;
        return node.connectionPoint;
    }

    //-------------------------------------------------------------------------
    //aとbの2点のチャンクを結ぶ通路チャンクの場所をL字で計算し、cdにそのデータを入れる
    //-------------------------------------------------------------------------
    void createLjiLineCorridor(Vector2Int a, Vector2Int b, ChunkData cd, Vector2Int chunkKey,  bool createLjimode = true)
    {
        if(createLjimode){
            int hvFlag = Mathf.Abs(Hash(2, 2, a.x * 7 + b.y, (int)HashPurpose.CorridorCorner)) % 2;
            Vector2Int corner = (hvFlag == 0) ? new Vector2Int(b.x, a.y) : new Vector2Int(a.x, b.y);
            createLjiLineCorridor(a, corner, cd, chunkKey, false);
            createLjiLineCorridor(corner, b, cd, chunkKey, false);
        }
        else{
            int dx = (b.x == a.x) ? 0 : (b.x > a.x ? 1 : -1);
            int dy = (b.y == a.y) ? 0 : (b.y > a.y ? 1 : -1);
            //使用するprefabtypeを指定
            int corridorSeedId = Hash(a.x, b.y, 343, (int)HashPurpose.CorridorTypeSelect);
            prefab_type pt = corridorPrefabs[RandomSelect.GetRandomIndex(corridorPrefabs, b => b.placementChance, corridorSeedId, cd.chunkBiome.useCorridor_Index)];

            Vector2Int cur = a;
            Vector2Int end = new Vector2Int(b.x + dx, b.y + dy);
            Vector2Int start = a;  // 始点を保存
            Vector2Int last = b;   // 終点を保存

            while (cur != end)
            {
                if (!corriderCells.ContainsKey(cur))
                {
                    corriderCells[cur] = new CellData();
                    corriderCells[cur].cellType = CellType.Corridor;
                    corriderCells[cur].prefabType = pt;

                    if (corriderCells[cur].connectDir == null)
                    {
                        corriderCells[cur].connectDir = new Dictionary<Vector2Int, bool>()
                        {
                            { new Vector2Int(0, 1), false },
                            { new Vector2Int(0, -1), false },
                            { new Vector2Int(1, 0), false },
                            { new Vector2Int(-1, 0), false }
                        };
                    }

                    // 始点と終点はすべてtrue、それ以外は通路方向のみtrue
                    if (cur == start || cur == last)
                    {
                        corriderCells[cur].connectDir[new Vector2Int(0, 1)] = true;
                        corriderCells[cur].connectDir[new Vector2Int(0, -1)] = true;
                        corriderCells[cur].connectDir[new Vector2Int(1, 0)] = true;
                        corriderCells[cur].connectDir[new Vector2Int(-1, 0)] = true;
                    }
                    else
                    {
                        corriderCells[cur].connectDir[new Vector2Int(dx, dy)] = true;
                        corriderCells[cur].connectDir[new Vector2Int(dx*-1, dy*-1)] = true;
                    }
                }
                

                cur += new Vector2Int(dx, dy);
            }
        }
    }
    
    
    #endregion

    #region Deterministic random hash
    public static int Hash(int x, int z, int id, int purpose)
    {
        unchecked
        {
            uint h = (uint)baseSeed;
            h ^= (uint)x * 2747636419u;
            h *= 2654435761u;
            h ^= (uint)z * 2836284541u;
            h *= 2654435761u;
            h ^= (uint)id * 2246822519u;
            h *= 2654435761u;
            h ^= (uint)purpose;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)h;
        }
    }
    #endregion
}