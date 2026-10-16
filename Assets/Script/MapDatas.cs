using UnityEngine;
using System.Collections.Generic;

public enum CellType
{
    Room,
    Corridor,
    Entry,
    Exitgyahaha,
    Exit
}
public class FloorData //一つの階層のデータ
{
    public int floor;
    public Vector2Int entranceChunk;
    public Vector2Int exitChunk;
    public Vector2Int eventChunk;
    public bool entryDecided;
    public Vector2Int entryCell;
    public BSPMapGenerator.prefab_type entryPrefab;
    public Vector2Int exitCell;
    public BSPMapGenerator.prefab_type exitPrefab;
    public bool exitDecided;
}

public class ChunkData//複数セルのまとまり
{
    public int chunkId;
    public bool generatedCellDatas = false;
    public BSPMapGenerator.chunkBiome_Type chunkBiome;
    public List<RoomData> RoomList;
    public Dictionary<Vector2Int,CellData> cellDatas;
    public List<Vector2Int> roomConnectionPoints = new List<Vector2Int>();
    public Vector2Int connectionPoint;
    public Dictionary<Vector2Int, bool> chunkConnectDirs = new Dictionary<Vector2Int, bool>{}; //trueの方向のchunkにのみ道を生成する
    
}

public class RoomData//それぞれの部屋の位置とサイズ
{
    public int roomId = 0;
    public bool _sizeLimit = true;
    public bool iseventRoom; // 出口部屋なら true
    public BSPMapGenerator.prefab_type pt; 
    public Vector2Int roomStartcell, roomEndcell;
    public List<Vector2Int> keikoutouSwitchPlaceableKey = new();
}
    
public class BSPLeafData//BSP用それぞれの葉のデータ
{
    public Vector2Int leafStartcell,leafEndcell,connectionPoint;
    public BSPLeafData parent,left,right;
    public RoomData room;
    public bool IsLeaf => left == null && right == null;
}

public class CellData //基礎データ
{
    public int cellId = 0, roomId, keikoutou_xnum = 2,keikoutou_znum = 1;
    public bool generatedData;
    
    public Vector2Int thiscellKey;
    public CellType cellType;
    public BSPMapGenerator.prefab_type prefabType; 
    public CellPrefabData cellPrefabData = new CellPrefabData();
    public RoomData room;
    public ChunkData chunk;
    
    public Dictionary<Vector2Int, bool> connectDir;
}
public class CellPrefabData //基礎データをもとに生成したもっと細かいデータ
{
    public bool placePipes = true,placeKeikoutou = true;
    public Quaternion placeRotate = Quaternion.identity;
    public GameObject entryMainPrefab,keikoutouSwitchPositionsParent = null;
    public Quaternion propsPositionRotate = Quaternion.Euler(0, 0, 0);
    public List<Vector2Int> blockedCells = new List<Vector2Int>();
    public Dictionary<string, MeshCombiner.CombineType> combineTypeMap = new Dictionary<string, MeshCombiner.CombineType>();//メッシュ統合するときに条件によって統合したくないものなどの情報の辞書
    public Dictionary<string, Vector2Int> ChangeUVMap = new Dictionary<string, Vector2Int>();//メッシュ統合するときにUVの位置を変えたいオブジェクト名とUV座標の辞書 ]
    public Dictionary<string, List<Transform>> objPositions = new Dictionary<string, List<Transform>>();//オブジェクトを配意する位置のリスト
    public List<string> otherObjectNames = new List<string>();

    //randomPlaceObjects
    public bool doRandomPlaceObjects = false;
    public int randomPlaceObjectChance = 80;
    public List<string> randomPlaceObjectNames = new List<string>();
}
public class otherObjectData
{
    public GameObject gameObject;
    public Vector3 position = new Vector3(0,0,0);
    public Quaternion rotation = Quaternion.identity;
    public bool isAnomaly; 
    public int key; 
}