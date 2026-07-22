using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Room : MonoBehaviour
{
    public enum entryPrefabType
    {
        Normal,
        Vertical,
        Tshape,
        Incorner,
        IncornerCross,
        Outcorner
    }

    [System.Serializable]
    public class entryPrefabGroup
    {
        public int prefabId;
        public entryPrefabType entryPrefabType;
        public GameObject prefab;
        [Range(0, 100)] public int placementChance;
    }

    //------取得するコンポーネント------
    public static KeikoutouManager keikoutouComponent;
    //public static RoomPipe pipeComponent;
    public static BSPMapGenerator mapGenerator;
    public static VertexShadowBaker baker;

    //-----------static変数------------
    protected static Dictionary<Vector2Int, int> RoomCellPrefabId = new Dictionary<Vector2Int, int>();
    protected static GameObject emptyObj;

    //--------インスタンス変数----------
    public Vector2Int thiscellKey;
    public int cellId;
    public bool shadowBakeReady = false,canDelete = false;
    protected GameObject PlacePrefab;
    protected GameObject subRoomObj;
    protected Mesh mesh;
    protected Material[] mats;
    protected Mesh mainRoomMesh;
    protected Material[] mainRoomMats;
    protected List<CombineInstance> subRoomMeshs = new();
    protected List<Material> subRoomMats = new(); 
    [HideInInspector] public BSPMapGenerator.cellData cellData;    

    public List<entryPrefabGroup> prefabGroups = new List<entryPrefabGroup>();
    public List<Vector2Int> blockedCells = new();//パイプ生成で通ってほしくないセルの座標リスト   

    protected Dictionary<entryPrefabType, List<(GameObject prefab, int placementChance)>> entryPrefabMap;
    protected Dictionary<Vector2Int, BSPMapGenerator.cellData> adjacentcellDatas = new Dictionary<Vector2Int, BSPMapGenerator.cellData>();//自身のチャンクと隣接するチャンクのデータのセット
    public Dictionary<string, MeshCombiner.CombineType> combineTypeMap = new Dictionary<string, MeshCombiner.CombineType>();//メッシュ統合するときに条件によって統合したくないものなどの情報の辞書
    public Dictionary<string, Vector2Int> ChangeUVMap = new Dictionary<string, Vector2Int>{};//メッシュ統合するときにUVの位置を変えたいオブジェクト名とUV座標の辞書 

    protected Vector2Int[] _neighborOffsets= new Vector2Int[]
    {
        new Vector2Int( 1,0),
        new Vector2Int(-1,0),
        new Vector2Int(0, 1),
        new Vector2Int(0,-1),
    };
    protected List<Vector2Int> diagonalDirectionOffset = new()
    {
        new Vector2Int( 1, 1),
        new Vector2Int( 1,-1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1,-1)
    };
    

    void Awake()
    {
        keikoutouComponent = FindObjectOfType<KeikoutouManager>();
        //pipeComponent = FindObjectOfType<RoomPipe>();
        mapGenerator = FindObjectOfType<BSPMapGenerator>();
        emptyObj = GameObject.Find("Empty");
    }

    //--------------------------------------------
    //部屋を作るときに必要なデータを取得、保存
    //--------------------------------------------
    public virtual void Init(){
        combineTypeMap = new Dictionary<string, MeshCombiner.CombineType>(MeshCombiner.DefaultCombineTypeMap);
        cellId = cellData.cellId;
        //一つの種類の部屋に必要なprefabをentryPrefabMapごとに別のリストにする
        entryPrefabMap = new Dictionary<entryPrefabType, List<(GameObject prefab, int placementChance)>>();
        foreach (var entry in prefabGroups)
        {
            if (!entryPrefabMap.TryGetValue(entry.entryPrefabType, out var list))
            {
                list = new List<(GameObject, int)>();
                entryPrefabMap[entry.entryPrefabType] = list;
            }
            list.Add((entry.prefab, entry.placementChance));
        }

        //隣接するcellDataの辞書
        adjacentcellDatas[_neighborOffsets[0]] = mapGenerator.getCellData(thiscellKey + _neighborOffsets[0]);
        adjacentcellDatas[_neighborOffsets[1]] = mapGenerator.getCellData(thiscellKey + _neighborOffsets[1]);
        adjacentcellDatas[_neighborOffsets[2]] = mapGenerator.getCellData(thiscellKey + _neighborOffsets[2]);
        adjacentcellDatas[_neighborOffsets[3]] = mapGenerator.getCellData(thiscellKey + _neighborOffsets[3]);
        adjacentcellDatas[new Vector2Int(1,1)]   = mapGenerator.getCellData(thiscellKey + new Vector2Int(1,1));
        adjacentcellDatas[new Vector2Int(-1,1)]  = mapGenerator.getCellData(thiscellKey + new Vector2Int(-1,1));
        adjacentcellDatas[new Vector2Int(1,-1)]  = mapGenerator.getCellData(thiscellKey + new Vector2Int(1,-1));
        adjacentcellDatas[new Vector2Int(-1,-1)] = mapGenerator.getCellData(thiscellKey + new Vector2Int(-1,-1));
      
        PlacePrefab = emptyObj;
        subRoomObj = new GameObject($"subRoomObj");
        subRoomObj.transform.SetParent(transform, false);

        //テストログ用
        string log = $"チャンク：{thiscellKey}";
        foreach (var offset in _neighborOffsets)
        {
            var cell = adjacentcellDatas.ContainsKey(offset) ? adjacentcellDatas[offset] : null;
            log += $"|{offset}：{(cell != null ? cell.cellType.ToString() : "null")}";
        }
        //Debug.Log(log);
    }

    //--------------------------------------------
    //Roomを継承した子クラスないで使用する関数。上から下の順で実行することが前提
    //--------------------------------------------
    //壁のある方向に部屋があったら壁を消すを4方向行う
    public void addNoCombineWall_ForAdjacentRooms(){
        if (adjacentcellDatas[_neighborOffsets[0]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[0]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[0]] != true && (adjacentcellDatas[_neighborOffsets[0]].cellType == BSPMapGenerator.CellType.Corridor )))){
            combineTypeMap["wall3"] = MeshCombiner.CombineType.NoCombine;
        }
        if (adjacentcellDatas[_neighborOffsets[1]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[1]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[1]] != true && (adjacentcellDatas[_neighborOffsets[1]].cellType == BSPMapGenerator.CellType.Corridor )))){
            combineTypeMap["wall4"] = MeshCombiner.CombineType.NoCombine;
        }
        if (adjacentcellDatas[_neighborOffsets[2]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[2]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[2]] != true && (adjacentcellDatas[_neighborOffsets[2]].cellType == BSPMapGenerator.CellType.Corridor )))){
            combineTypeMap["wall1"] = MeshCombiner.CombineType.NoCombine;
        }
        if (adjacentcellDatas[_neighborOffsets[3]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[3]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[3]] != true && (adjacentcellDatas[_neighborOffsets[3]].cellType == BSPMapGenerator.CellType.Corridor )))){
            combineTypeMap["wall2"] = MeshCombiner.CombineType.NoCombine;
        }
    }

    //mainRoomのメッシュ統合と基本となるコンポーネントの追加を行い、mainRoomのメッシュとマテリアルを適応する。また、mainRoom内にsubroom用のオブジェクトがあった場合、統合せずデータとして追加しておく「
    public void InitializeMainRoomPrefab(){
        if(PlacePrefab == emptyObj)return;
        
        List<CombineInstance> _subRoomMeshs = new();
        Material[] _subRoomMats;
        (mainRoomMesh, mainRoomMats, _subRoomMeshs, _subRoomMats) = MeshCombiner.CombinePrefabMeshes(PlacePrefab, combineTypeMap, ChangeUVMap);
        if(_subRoomMeshs!=null){
            subRoomMeshs.AddRange(_subRoomMeshs);
            subRoomMats.AddRange(_subRoomMats);
        }
        //subRoommeshなどに追加する処理

        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null)mf = gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null)mr = gameObject.AddComponent<MeshRenderer>();
        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null)mc = gameObject.AddComponent<MeshCollider>();

        mf.sharedMesh = mainRoomMesh;
        mr.sharedMaterials = mainRoomMats ?? new Material[0];
        mc.sharedMesh = null;
        mc.sharedMesh = mainRoomMesh;
    }

    //public void placePipe(){
    //    if (pipeComponent != null && cellData.prefabType.placePipes)
    //    {
    //        var (_mesh, _mat) = pipeComponent.GeneratePipes(cellData.prefabType, thiscellKey, 0, blockedCells);
    //        if (_mesh != null)
    //        {
    //            subRoomMeshs.Add(new CombineInstance { mesh = _mesh, subMeshIndex = 0, transform = Matrix4x4.identity });
    //            subRoomMats.Add(_mat);
    //        }
    //        (_mesh, _mat) = pipeComponent.GeneratePipes(cellData.prefabType, thiscellKey, 1, blockedCells);
    //        if (_mesh != null)
    //        {
    //            subRoomMeshs.Add(new CombineInstance { mesh = _mesh, subMeshIndex = 0, transform = Matrix4x4.identity });
    //            subRoomMats.Add(_mat);
    //        }          
    //    }else if(pipeComponent == null){
    //        Debug.Log("pipeComponent == null");
    //    }
    //}
    public void placeRoomKeikoutou(int xnum = 2,int znum = 1){
        if(keikoutouComponent != null && PlacePrefab != emptyObj )
        {
            keikoutouComponent.placeKeikoutou(cellData.prefabType,transform, xnum,znum);
        } 
    }
    public void InitializeSubRoom()
    {
        var (finalMesh, finalMats) = MeshCombiner.CombineMeshesMaterials(subRoomMeshs, subRoomMats.ToArray());

        subRoomObj.AddComponent<MeshFilter>().sharedMesh = finalMesh;
        subRoomObj.AddComponent<MeshRenderer>().sharedMaterials = finalMats;        
    }
    //shadowbake
    public void room_ShadowBake()
    {
        baker = gameObject.AddComponent<VertexShadowBaker>();
        baker.Bake();
        shadowBakeReady = false;
    }

}