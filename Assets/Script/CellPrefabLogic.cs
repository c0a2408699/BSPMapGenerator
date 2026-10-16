using UnityEngine;
using System.Collections.Generic;



public class CellPrefabLogic : MonoBehaviour
{
    public enum entryPrefabType
    {
        Normal,
        RandomWall,
        Vertical,
        Tshape,
        Incorner,
        IncornerCross,
        Outcorner,
        WithWall,
        Other
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
    [HideInInspector] public CellData cellData;
    [HideInInspector] public BSPMapGenerator.prefab_type prefabType;
    
    public static BSPMapGenerator mapGenerator;
    

    
    protected GameObject emptyObj;

    // 変更: 全インスタイル共通で内容が変わらないため static readonly 化(インスタンスごとの重複生成・保持を削減)
    protected static readonly List<Vector2Int> neighborOffsets_1 = new()
    {
        new Vector2Int(0, 1),
        new Vector2Int(0,-1),
        new Vector2Int( 1,0),
        new Vector2Int(-1,0),
    };
    protected static readonly List<Vector2Int> neighborOffsets_2 = new()
    {
        new Vector2Int( 1, 1),
        new Vector2Int( 1,-1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1,-1)
    };
    //-----------入力する変数-----------
    public List<entryPrefabGroup> prefabGroups = new List<entryPrefabGroup>();
    [Header("RandomPlaceObjects")]
    [SerializeField] private bool doRandomPlaceObjects = false;
    [Range(0, 100)] public int randomPlaceObjectChance = 80;
    
    //--------インスタンス変数----------
     //public bool isGenerating = true;
    public Vector2Int thiscellKey;

    // 変更: Init()のたびに new() していたコレクションをフィールド初期化子で1度だけ生成し、
    // Init() 内では Clear() して使い回す(GenerateCellDatas から毎セル呼ばれるためGC負荷が大きかった)
    protected List<Vector2Int> blockedCells = new List<Vector2Int>();//パイプ生成で通ってほしくないセルの座標リスト   

    protected Dictionary<Vector2Int, CellData> adjacentcellDatas = new Dictionary<Vector2Int, CellData>();//自身のチャンクと隣接するチャンクのデータのセット
    
    //  entryPrefabMap は prefabGroups(Inspectorで設定され実行中は変化しない想定)から組み立てるだけなので、毎Init()で再構築せず Awake() で一度だけ構築してキャッシュする
    protected Dictionary<entryPrefabType, List<(GameObject prefab, int placementChance)>> entryPrefabMap;
    private bool entryPrefabMapBuilt = false;
    



    void Awake()
    {
        mapGenerator = BSPMapGenerator.Instance;
        BuildEntryPrefabMap();
    }
    private void BuildEntryPrefabMap()
    {
        if (entryPrefabMapBuilt) return;

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

        entryPrefabMapBuilt = true;
    }


    //------------------------------------------------
    //部屋を生成するためのデータを計算し、保存する関数呼ぶ
    //------------------------------------------------
    public virtual void Init(){
        if (mapGenerator == null)
            mapGenerator = BSPMapGenerator.Instance;

        if (mapGenerator == null)
        {
            Debug.LogError("BSPMapGenerator.Instance が設定されていません。");
            return;
        }
        cellData.cellPrefabData.combineTypeMap = new Dictionary<string, MeshCombiner.CombineType>(MeshCombiner.DefaultCombineTypeMap);
        if(cellData.thiscellKey == null)
        {
            cellData.thiscellKey =thiscellKey;
        }
        else
        {
            thiscellKey = cellData.thiscellKey;
        }
        // 変更: 毎回 new() せず、使い回すコレクションを Clear() するだけにする
        blockedCells.Clear();
    
        adjacentcellDatas.Clear();
    

        

        // 変更: entryPrefabMapの構築はAwakeで完了済みなのでここでは何もしない

        //隣接するcellDataの辞書
        foreach(var keyOffset in neighborOffsets_1)
        {
            adjacentcellDatas[keyOffset] = mapGenerator.getBaseCellData(thiscellKey + keyOffset);
        }
        foreach(var keyOffset in neighborOffsets_2)
        {
            adjacentcellDatas[keyOffset] = mapGenerator.getBaseCellData(thiscellKey + keyOffset);
        }
        

        addNoCombineWall_ForAdjacentRooms();
        ConfigurePrefabPlacement();
        cellData.cellPrefabData.doRandomPlaceObjects = doRandomPlaceObjects; 
        cellData.cellPrefabData.randomPlaceObjectChance = randomPlaceObjectChance; 
    
       
        
        cellData.generatedData = true;
    }


    //---------------------------------------------------------------------------
    //配置するprefabのルールを決める関数。独自の配置ルールの場合はオーバーライドして使用する
    //---------------------------------------------------------------------------
    public virtual void ConfigurePrefabPlacement()
    {
        cellData.cellPrefabData.entryMainPrefab = entryPrefabMap[entryPrefabType.Normal][0].prefab;
    }

    public void addNoCombineWall_ForAdjacentRooms(){//壁のある方向に部屋があったら壁を消す
        if(isConnectCell(neighborOffsets_1[0]))
        {
            cellData.cellPrefabData.combineTypeMap["wall1"] = MeshCombiner.CombineType.NoCombine;
        }
        if(isConnectCell(neighborOffsets_1[1]))
        {
            cellData.cellPrefabData.combineTypeMap["wall2"] = MeshCombiner.CombineType.NoCombine;
        }
        if(isConnectCell(neighborOffsets_1[2]))
        {
            cellData.cellPrefabData.combineTypeMap["wall3"] = MeshCombiner.CombineType.NoCombine;
        }
        if(isConnectCell(neighborOffsets_1[3]))
        {
            cellData.cellPrefabData.combineTypeMap["wall4"] = MeshCombiner.CombineType.NoCombine;
        }
    }
    protected bool isConnectCell(Vector2Int cellKey)
    {
        if(adjacentcellDatas[cellKey] != null
        && (cellData.connectDir == null
        || cellData.connectDir[cellKey]
        || (!cellData.connectDir[cellKey] && (adjacentcellDatas[cellKey].cellType == CellType.Corridor ))))
        {
            return true;
        }
        return false;
    } 
    protected bool isThisPrefabType(Vector2Int cellKey)
    {
        if(adjacentcellDatas[cellKey] != null
        && (cellData.connectDir == null
        || cellData.connectDir[cellKey]
        || (!cellData.connectDir[cellKey] && (adjacentcellDatas[cellKey].cellType == CellType.Corridor ))))
        {
            return true;
        }
        return false;
    }
    
    protected void addOterObject(string objname)
    {
        cellData.cellPrefabData.otherObjectNames.Add(objname);
        cellData.cellPrefabData.combineTypeMap[objname] = MeshCombiner.CombineType.NoCombine;    
    }
    protected void removeOterObject(string objname)
    {
        cellData.cellPrefabData.otherObjectNames.Remove(objname);
        cellData.cellPrefabData.combineTypeMap[objname] = MeshCombiner.CombineType.NoCombine;    
    }
    protected bool isRoomEdge()
    {
        int thisPrefabLogicNum = 0;
        foreach(var dir1 in neighborOffsets_1)
        {
            if(adjacentcellDatas[dir1] != null && adjacentcellDatas[dir1].prefabType.PrefabLogic == cellData.prefabType.PrefabLogic)
            {
                thisPrefabLogicNum++;
            }
        }
        if(thisPrefabLogicNum!=4)
        {
            return true;
        }
        else{
            foreach(var dir2 in neighborOffsets_2)
            {
                if(adjacentcellDatas[dir2] != null && adjacentcellDatas[dir2].prefabType.PrefabLogic == cellData.prefabType.PrefabLogic)
                {
                    thisPrefabLogicNum++;
                }
            }
            if(thisPrefabLogicNum == 8)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
    protected bool isRoomEdge(Vector2Int cellKey)
    {
        int thisPrefabLogicNum = 0;
        foreach(var dir1 in neighborOffsets_1)
        {
            Vector2Int checkCell = cellKey + dir1;

            if( mapGenerator.getBaseCellData(checkCell) != null && mapGenerator.getBaseCellData(checkCell).prefabType.PrefabLogic == cellData.prefabType.PrefabLogic)
            {
                thisPrefabLogicNum++;
            }
        }
        if(thisPrefabLogicNum!=4)
        {
            return true;
        }
        else{
            foreach(var dir2 in neighborOffsets_2)
            {
                Vector2Int checkCell = cellKey + dir2;
                if(mapGenerator.getBaseCellData(checkCell) != null && mapGenerator.getBaseCellData(checkCell).prefabType.PrefabLogic == cellData.prefabType.PrefabLogic)
                {
                    thisPrefabLogicNum++;
                }
            }
            if(thisPrefabLogicNum == 8)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
    protected void randomRotate(string name,bool rotatePositon = false)
    {
        int rotate = Mathf.Abs(Hash(thiscellKey, 1, 1)) % 4;
        if(rotate == 0)
        {
            cellData.cellPrefabData.combineTypeMap[name] = MeshCombiner.CombineType.rotate90z;
            if(rotatePositon)
            {
                cellData.cellPrefabData.propsPositionRotate = Quaternion.Euler(0, 0, 0);
            }
        }
        else if(rotate == 1)
        {
            cellData.cellPrefabData.combineTypeMap[name] = MeshCombiner.CombineType.rotate180z;
            if(rotatePositon)
            {
                cellData.cellPrefabData.propsPositionRotate = Quaternion.Euler(0, 90, 0);
            }
        }
        else if(rotate == 2)
        {
            cellData.cellPrefabData.combineTypeMap[name] = MeshCombiner.CombineType.rotate270z;
            if(rotatePositon)
            {
                cellData.cellPrefabData.propsPositionRotate = Quaternion.Euler(0, 270, 0);
            }
            
        }
        else 
        {
            if(rotatePositon)
            {
                cellData.cellPrefabData.propsPositionRotate = Quaternion.Euler(0,180, 0);
            }
        }
    }

    


    public int Hash(Vector2Int _cellkey, int id, int purpose)
    {
        unchecked
        {
            uint h = (uint)BSPMapGenerator.baseSeed;
            h ^= (uint)_cellkey.x * 2747636419u;
            h *= 2654435761u;
            h ^= (uint)_cellkey.y * 2836284541u;
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