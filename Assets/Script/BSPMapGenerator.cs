using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using System.Collections;

//----------------------BSPMapGeneratorマップ生成----------------------
//BSP法を用いてダンジョンを無限に自動生成するシステムです。プレイヤーの現在位置を基準に、一定サイズの「チャンク」単位でマップデータを遅延生成し、必要な範囲のみを動的に配置・破棄することでメモリと処理負荷を抑えています。
//マップ生成は、座標とシード値から一意な乱数を得られるハッシュ関数をもとに完全決定論的に行われ、同じシードであれば常に同じマップが再現されます。各チャンクはBSPアルゴリズムで再帰的に空間分割され、分割された領域ごとに部屋を配置し、木構造を辿って全ての部屋を通路で接続します。
//--------------------------------------------------------------------

//注意
//prefabに設定するオブジェクトはinspectorのmodel→read/write enabled にチェックを入れてないとnullになる

public class BSPMapGenerator : MonoBehaviour
{
    public enum CellType
    {
        Room,
        Corridor
    }

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
        ConnectionPointX = 16,
        ConnectionPointY = 17,
        CorridorPick = 18,
        CorridorCorner = 19,
        CorridorTypeSelect = 20      
    }

    [System.Serializable]
    public class prefab_type
    {
            public CellType cellType;
        public GameObject baseObject;//Roomを継承したスクリプトがコンポーネントにある空オブジェクト。実際に設置するオブジェクトはRoomを継承したスクリプト内で設定する
        [Range(0, 100)] public int placementChance = 80;
        public int x_widthmin = 2;
        public int x_widthmax = 3;
        public int z_widthmin = 2;
        public int z_widthmax = 3;
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

    public class cellData//一つのcellのデータ
    {
        public int cellId = 0;
        public CellType cellType;
        public prefab_type prefabType;
        public Dictionary<Vector2Int,bool> RoomConnectDirs; //Room2で使用する。個々の値が入っている場合、trueの方向のみ壁を壊すかを判定する
        public Vector2Int roomStartcell, roomEndcell;
    }

    public class chunkData//チャンクデータのひとつの塊
    {
        public int chunkId;
        public chunkBiome_Type chunkBiome;
        public List<RoomData> RoomDatas;
        public Dictionary<Vector2Int,cellData> cellDatas;
        public List<BSPLeafData> BSPLeafDatas;
        public Vector2Int connectionPoint;
        public Dictionary<Vector2Int, bool> chunkConnectDirs = new Dictionary<Vector2Int, bool>{}; //trueの方向のchunkにのみ道を生成する
    }

    public class RoomData//それぞれの部屋の位置とサイズ
    {
        public prefab_type pt; 
        public Vector2Int roomStartcell, roomEndcell;
        public bool _sizeLimit = true;
    }
    
    public class BSPLeafData//BSP用それぞれの葉のデータ
    {
        public Vector2Int leafStartcell,leafEndcell,connectionPoint;
        public BSPLeafData parent,left,right;
        public RoomData room;
        public bool IsLeaf => left == null && right == null;
    }


    //------------入力する変数------------
    public Transform player;//マップ生成の中心
    public Transform roomParent;//部屋のオブジェクトを置く親
    public int takasa = 10;//部屋オブジェクトを置く高さ
    public int placecellRadius = 10;//描画距離
    public int chunkcellSize = 10;//チャンクのサイズ
    public int inputSeed = 0;//シード値

    public List<prefab_type> roomPrefabs = new();
    public List<prefab_type> corridorPrefabs = new();
    public List<chunkBiome_Type> chunkBiomeTypes = new();

    //--------その他のインスタンス変数----------
    public static BSPMapGenerator Instance { get; private set; }
    [HideInInspector] public static int cellSize = 16;
    public static int baseSeed;
    private readonly HashSet<Vector2Int> _generatecells = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _removecells = new List<Vector2Int>();
    private Dictionary<Vector2Int, chunkData> chunkDatas = new Dictionary<Vector2Int, chunkData>();
    private Dictionary<Vector2Int, GameObject> cellObjects = new Dictionary<Vector2Int, GameObject>();
    private Dictionary<Vector2Int, Room> cellRoomScripts = new Dictionary<Vector2Int, Room>();
    private Vector2Int lastPlayerCell = new Vector2Int(int.MinValue, int.MinValue);
    private List<Vector2Int> directionOffset = new()
    {
        new Vector2Int(0, 1),
        new Vector2Int(0,-1),
        new Vector2Int(1, 0),
        new Vector2Int(-1,0)
    };
    private Vector2Int localroomStartcell = Vector2Int.zero;
    private Vector2Int localroomStartcell2 = Vector2Int.zero;


    void Awake()
    {
        if(inputSeed == 0)
        {
            baseSeed = Random.Range(int.MinValue, int.MaxValue);
            inputSeed = baseSeed;
        }
        else baseSeed = inputSeed;
    }

    void Start()
    {
        Vector2Int currentcell = WorldTocell(player.position);
        lastPlayerCell = currentcell;
        updateCells(currentcell); // 初回生成(この時点ではshadowBakeされない)
        StartCoroutine(initialShadowBakeCoroutine(currentcell));
    }

    void Update()
    {
        Vector2Int currentcell = WorldTocell(player.position);
        if (currentcell != lastPlayerCell)
        {
            lastPlayerCell = currentcell;
            updateCells(currentcell);
        }
    }
    
    private IEnumerator initialShadowBakeCoroutine(Vector2Int cell)
    {
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }
        updateCells(cell); // 生成済みセルのshadowBakeReadyを再チェックしてbakeする
    }

    private void OnValidate()
    {
        if(roomPrefabs != null)
        {
            foreach(var p in roomPrefabs)
            {
                if(p != null)
                    p.cellType = CellType.Room;
            }
        }

        if(corridorPrefabs != null)
        {
            foreach(var p in corridorPrefabs)
            {
                if(p != null)
                    p.cellType = CellType.Corridor;
            }
        }
    }

    Vector2Int WorldTocell(Vector3 pos)
    {
        return new Vector2Int(Mathf.FloorToInt(pos.x / cellSize),Mathf.FloorToInt(pos.z / cellSize));
    }

    public Vector2Int cellToChunk(Vector2Int cell)//チャンク/チャンクサイズ(あまり切り捨て)* チャンクサイズ
    {
        return new Vector2Int(Mathf.FloorToInt((float)cell.x / chunkcellSize) * chunkcellSize,Mathf.FloorToInt((float)cell.y / chunkcellSize) * chunkcellSize);
    }

    //----------------------------------------------------------
    //getCellData()を使用してplacecellRadius(描画距離)内のセルを配置する。
    //----------------------------------------------------------

    void updateCells(Vector2Int playercell)
    {
        for (int dx = -placecellRadius; dx <= placecellRadius; dx++)
        {
            for (int dz = -placecellRadius; dz <= placecellRadius; dz++)
            {
                Vector2Int cell = new Vector2Int(playercell.x + dx, playercell.y + dz);
                _generatecells.Add(cell);

                if (!cellObjects.ContainsKey(cell))
                {
                    cellData cellData = getCellData(cell);
                    if (cellData == null)continue;

                    Vector3 pos = new Vector3(cell.x * cellSize, takasa, cell.y * cellSize);
                    if (cellData == null)continue;
              
                    GameObject obj = Instantiate(cellData.prefabType.baseObject, pos, Quaternion.identity, roomParent);
                    cellObjects[cell] = obj;

                    //Roomクラスに情報を与える
                    Room roomComponent = obj.GetComponent<Room>();
                    
                    if (roomComponent != null)
                    {
                        roomComponent.thiscellKey = cell;
                        roomComponent.cellData = cellData;
                        roomComponent.Init();   
                        cellRoomScripts[cell] = roomComponent;
                    }
                }
                if(dx<4 && dx>-4 && dx>-placecellRadius+1 && dx<placecellRadius-1 && dz<4 && dz>-4 && dz>-placecellRadius+1 && dz<placecellRadius-1 && cellRoomScripts[cell].shadowBakeReady)
                {
                    if (cellRoomScripts.TryGetValue(cell, out Room room))
                    {
                        room.room_ShadowBake();
                    }
                }
            }
        }
        foreach (var kv in cellObjects)
        {
            if (!_generatecells.Contains(kv.Key))
                _removecells.Add(kv.Key);
        }
        foreach (var key in _removecells)
        {
            Destroy(cellObjects[key]);
            cellObjects.Remove(key);
        }
        _removecells.Clear();
        _generatecells.Clear();
    }

    public cellData getCellData(Vector2Int cell)
    {
        Vector2Int chunk = cellToChunk(cell);
        chunkData targetchunkdata = getOrCreatechunkData(chunk);
        //4方向すべてのソースチャンクへ通路接続
        foreach(var dir in directionOffset){
            Vector2Int targetchunkKey = new Vector2Int(chunk.x+(dir.x*chunkcellSize),chunk.y+(dir.y*chunkcellSize));
            Vector2Int p1 = chunk;
            Vector2Int p2 = targetchunkKey;
            chunkData targetchunkdata2 = getOrCreatechunkData(p2);
            //Debug.Log($"{targetchunkdata.chunkConnectDirs[dir]}：{targetchunkdata2.chunkConnectDirs[dir]}");
            if (p1.x > p2.x || (p1.x == p2.x && p1.y > p2.y))
            {
                (p1, p2) = (p2, p1);
            }   
            if(!getOrCreatechunkData(p1).chunkConnectDirs[dir]&&!getOrCreatechunkData(p2).chunkConnectDirs[dir]) continue;
            createLjiLineCorridor(getOrCreatechunkData(p1).connectionPoint,getOrCreatechunkData(p2).connectionPoint,targetchunkdata,chunk);
        }
        //そのチャンクに部屋がああるかどうか判定して返す
        int _roomId = 0;
        foreach (var room in  targetchunkdata.RoomDatas)
        {
            _roomId +=1;   
            if (cell.x >= room.roomStartcell.x && cell.x <= room.roomEndcell.x && cell.y >= room.roomStartcell.y && cell.y <= room.roomEndcell.y)
            {
                return new cellData
                {
                    cellId = targetchunkdata.chunkId+_roomId,
                    cellType = CellType.Room,
                    prefabType = room.pt,
                    roomStartcell = room.roomStartcell,
                    roomEndcell = room.roomEndcell
                };
            }
        }

        if (targetchunkdata.cellDatas.ContainsKey(cell))
        {
            cellData _cellData = targetchunkdata.cellDatas[cell];
            switch (_cellData.cellType)
            {
                case CellType.Corridor:
                    return new cellData
                    {
                        cellId = targetchunkdata.chunkId,
                        cellType = CellType.Corridor,
                        prefabType = _cellData.prefabType,
                        RoomConnectDirs = _cellData.RoomConnectDirs
                    };
            }
        }
        return null;
    }

    //----------------------------------------------------------
    //ソースサイズ内のマップを生成して返す。すでに生成していたらそのデータを返す
    //----------------------------------------------------------
    public chunkData getOrCreatechunkData(Vector2Int chunk)
    {   
        if (chunkDatas.ContainsKey(chunk)) return chunkDatas[chunk];
        
        //使用するチャンクのバイオームタイプを決定
        int chunkBiome_Typeseed = Hash(chunk.x, chunk.y, 343, (int)HashPurpose.ChunkBiomeSelect);
        chunkBiome_Type targetchunkBiome = chunkBiomeTypes[ChooseForChunce.GetRandomIndex(chunkBiomeTypes, b => b.placementChance, chunkBiome_Typeseed)];

        //chunkDataを準備
        chunkData newChunkData = new chunkData
        {
            chunkId = (chunk.x*chunk.x+1)*1000,
            chunkBiome = targetchunkBiome,
            BSPLeafDatas = new List<BSPLeafData>(),
            cellDatas = new Dictionary<Vector2Int, cellData>(),
            chunkConnectDirs = new Dictionary<Vector2Int, bool>
            {
                { new Vector2Int( 0,  1), true },
                { new Vector2Int( 0, -1), true },
                { new Vector2Int(-1,  0), true },
                { new Vector2Int( 1,  0), true },
            }
        };

        BSPLeafData newLeafRoom = new BSPLeafData();
        newLeafRoom.leafStartcell = new Vector2Int(chunk.x,chunk.y);
        newLeafRoom.leafEndcell = new Vector2Int(chunk.x+chunkcellSize-2,chunk.y+chunkcellSize-2);
        newChunkData.BSPLeafDatas.Add(newLeafRoom);

        //chunkDataを分割
        int RoomDivisionNum = Mathf.Abs(Hash(chunk.x, chunk.y, 998, (int)HashPurpose.RoomDivisionNum)) % (targetchunkBiome.maxRoomNum - targetchunkBiome.minRoomNum + 1) + targetchunkBiome.minRoomNum;
        for(int i = 0; i < RoomDivisionNum; i++)
        {
            int index = Mathf.Abs(Hash(chunk.x, chunk.y, i, (int)HashPurpose.BSPLeafSelect + i * 10)) % newChunkData.BSPLeafDatas.Count;
            BSPLeafData targetBSPLeafData = newChunkData.BSPLeafDatas[index];
            newChunkData.BSPLeafDatas.RemoveAt(index);

            int divisionX = Mathf.Abs(Hash(chunk.x, chunk.y, i, (int)HashPurpose.BSPDivisionAxis + i * 10)) % 2;
            
            bool canDivisionX = targetBSPLeafData.leafEndcell.x - targetBSPLeafData.leafStartcell.x - 1 >= 4;//要修正？
            bool canDivisionZ = targetBSPLeafData.leafEndcell.y - targetBSPLeafData.leafStartcell.y - 1 >= 4;
            if (!canDivisionX && !canDivisionZ)
            {
                newChunkData.BSPLeafDatas.Add(targetBSPLeafData);
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
            newChunkData.BSPLeafDatas.Add(newLeafRoom1);
            newChunkData.BSPLeafDatas.Add(newLeafRoom2);
        }
        
        
        //分割した葉ノードそれぞれに部屋を作る
        int index2 = 0;
        var rooms = new List<RoomData>();
        foreach(var leafRoom in newChunkData.BSPLeafDatas)
        {
            int roomSeedId = Hash(chunk.x, chunk.y, index2, (int)HashPurpose.RoomTypeSelect);
            prefab_type pt = roomPrefabs[ChooseForChunce.GetRandomIndex(roomPrefabs, b => b.placementChance, roomSeedId, targetchunkBiome.useRoom_Index)];

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
            RoomData _room = new RoomData
            {
                roomStartcell = new Vector2Int(leafRoom.leafStartcell.x + localroomStartcell.x,leafRoom.leafStartcell.y + localroomStartcell.y),
                roomEndcell = new Vector2Int(roomEndX, roomEndZ),
                _sizeLimit = pt.sizeLimit,
                pt = pt
            };
            rooms.Add(_room);
            leafRoom.room = _room;

            if(!pt.doubleRoom) continue;

            index2++;
            int roomSizeX2 = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomSizeX + index2 * 10)) % (pt.x_widthmax - pt.x_widthmin + 1) + pt.x_widthmin;
            int roomSizeZ2 = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomSizeZ + index2 * 10)) % (pt.z_widthmax - pt.z_widthmin + 1) + pt.z_widthmin;
            localroomStartcell2.x = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomStartX + index2 * 10)) % (roomSizeX - pt.x_widthmin + 1);
            localroomStartcell2.y = Mathf.Abs(Hash(chunk.x+9, chunk.y+9, index2, (int)HashPurpose.DoubleRoomStartY + index2 * 10)) % (roomSizeZ - pt.z_widthmin + 1);
            roomEndX = leafRoom.leafStartcell.x + localroomStartcell.x + localroomStartcell2.x + roomSizeX2;
            roomEndZ = leafRoom.leafStartcell.y + localroomStartcell.y + localroomStartcell2.y + roomSizeZ2;
            if(roomEndX > leafRoom.leafEndcell.x && pt.sizeLimit) roomEndX = leafRoom.leafEndcell.x;
            if(roomEndZ > leafRoom.leafEndcell.y && pt.sizeLimit) roomEndZ = leafRoom.leafEndcell.y;
            RoomData __room = new RoomData
            {
                roomStartcell = new Vector2Int(
                    leafRoom.leafStartcell.x + localroomStartcell.x + localroomStartcell2.x,
                    leafRoom.leafStartcell.y + localroomStartcell.y + localroomStartcell2.y),
                roomEndcell = new Vector2Int(roomEndX, roomEndZ),
                _sizeLimit = pt.sizeLimit,
                pt = pt
            };
            rooms.Add(__room);
    
            
        }
        newChunkData.RoomDatas = rooms;
        

        //通路生成、全葉の親であるnewLeafRoomからすべての子へ通路を生成する
        GenerateCorridors(newLeafRoom, newChunkData, chunk);
        //ソースデータ同士で通路を作らない方向を決める
        if((chunk.x % 2 == 0) && (chunk.y % 2 == 0))
        {
            // directionOffsetをリスト化してシャッフル
            List<Vector2Int> dirs = new List<Vector2Int>(directionOffset);
            
            // ハッシュで1つ目を選ぶ
            int first = Mathf.Abs(Hash(chunk.x, chunk.y, 0, (int)HashPurpose.ChunkDisconnectDir1)) % dirs.Count;
            int second = Mathf.Abs(Hash(chunk.x, chunk.y, 0, (int)HashPurpose.ChunkDisconnectDir2)) % dirs.Count;
            newChunkData.chunkConnectDirs[directionOffset[first]] = false;
            newChunkData.chunkConnectDirs[directionOffset[second]] = false;
        }
        chunkDatas[chunk] = newChunkData;
        return newChunkData;
    }

    //----------------------------------------------------------
    //すべての葉ノードに接続点を作り、それらのペアをつなぐ関数を呼び出す
    //----------------------------------------------------------
    Vector2Int GenerateCorridors(BSPLeafData node, chunkData cd, Vector2Int chunk)
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
            cd.connectionPoint = _connectionPoint;
            return node.connectionPoint;
        }

        Vector2Int leftPoint  = GenerateCorridors(node.left,  cd, chunk);
        Vector2Int rightPoint = GenerateCorridors(node.right, cd, chunk);
        createLjiLineCorridor(leftPoint, rightPoint, cd, chunk);

        int pick = Mathf.Abs(Hash(chunk.x, chunk.y, leftPoint.x + rightPoint.y, (int)HashPurpose.CorridorPick)) % 2;
        node.connectionPoint = (pick == 0) ? leftPoint : rightPoint;
        return node.connectionPoint;
    }

    //-------------------------------------------------------------------------
    //aとbの2点のチャンクを結ぶ通路チャンクの場所をL字で計算し、cdにそのデータを入れる
    //-------------------------------------------------------------------------
    void createLjiLineCorridor(Vector2Int a, Vector2Int b, chunkData cd, Vector2Int chunkKey, bool createLjimode = true)
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
            int corridorSeedId = Hash(chunkKey.x, chunkKey.y, 343, (int)HashPurpose.CorridorTypeSelect);
            prefab_type pt = corridorPrefabs[ChooseForChunce.GetRandomIndex(corridorPrefabs, b => b.placementChance, corridorSeedId, cd.chunkBiome.useCorridor_Index)];
            

            Vector2Int cur = a;
            Vector2Int end = new Vector2Int(b.x + dx, b.y + dy);
            Vector2Int start = a;  // 始点を保存
            Vector2Int last = b;   // 終点を保存

            while (cur != end)
            {
                if (!cd.cellDatas.ContainsKey(cur))
                {
                    cd.cellDatas[cur] = new cellData();
                }
                cd.cellDatas[cur].cellType = CellType.Corridor;
                cd.cellDatas[cur].prefabType = pt;

                if (cd.cellDatas[cur].RoomConnectDirs == null)
                {
                    cd.cellDatas[cur].RoomConnectDirs = new Dictionary<Vector2Int, bool>()
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
                    cd.cellDatas[cur].RoomConnectDirs[new Vector2Int(0, 1)] = true;
                    cd.cellDatas[cur].RoomConnectDirs[new Vector2Int(0, -1)] = true;
                    cd.cellDatas[cur].RoomConnectDirs[new Vector2Int(1, 0)] = true;
                    cd.cellDatas[cur].RoomConnectDirs[new Vector2Int(-1, 0)] = true;
                }
                else
                {
                    cd.cellDatas[cur].RoomConnectDirs[new Vector2Int(dx, dy)] = true;
                    cd.cellDatas[cur].RoomConnectDirs[new Vector2Int(dx*-1, dy*-1)] = true;
                }

                cur += new Vector2Int(dx, dy);
            }
        }
    }
    
    
    int Hash(int x, int z, int id, int purpose)
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
}