using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// 1セル分の部屋・通路プレハブを組み立て、シーンへ配置するコンポーネント。
/// MeshCombiner と各種マネージャーを使って、床・壁・配管などのメッシュをまとめ、
/// 個別に扱う小物を親オブジェクト配下に生成する。生成したセルは activeCellDatas で追跡する。
///
/// セットアップ:
/// 1. シーン内の管理オブジェクトに本コンポーネントを追加し、Base Object と Room Parent を設定する。
/// 2. シーンに BSPMapGenerator、RoomPipe、KeikoutouManager などの依存コンポーネントを配置する。
/// 3. CellPrefabData の entryMainPrefab、メッシュ結合設定、配置対象名、セル種別を設定する。
/// 4. Entry セルのプレハブには ElevatorButton、ElevatorDoor1、ElevatorDoor2 を用意する。
///
/// 実行時の流れ:
/// - placeCellPrefab がメインメッシュとサブメッシュを準備し、必要な配管や小物を追加する。
/// - セルの GameObject を生成し、メッシュ・コライダー・ミニマップ情報を登録する。
/// - 生成完了後にシャドウベイク可能状態にし、pendingShadowBake に登録されたセルを処理する。
/// - DestroyCell でセルを破棄し、activeCellDatas から管理情報を取り除く。
///
/// 注意:
/// - CellData、CellPrefabData、MeshCombiner、RoomPipe、KeikoutouManager、MinimapTexture、
///   AnomalyObjectManager、GameEvent など、プロジェクト側の型・サービスを参照する。
/// - Prefab の子オブジェクト名を使う処理は、Inspector 上の名前と完全一致させること。
/// </summary>
public class PlaceCell : MonoBehaviour
{ 
    public class ActiveCelldata
    {
        public bool shadowBakeReady = false,shadowBaked = false, placedKeikoutouSwitch = false;
        public GameObject mainObj;
        public CellData activeCellData;
    }
    // Awakeの間に参照するコンポーネント
    protected GameObject emptyObj;
    static BSPMapGenerator mapGenerator;
    public static PlaceCell Instance;
    // インスペクターの設定
    public int takasa = 0;//部屋オブジェクトを置く高さ
    public GameObject baseObject;
    public Transform roomParent;//部屋のオブジェクトを置く親
    public Mesh entryMainRoomMesh;
    public Material[] entryMainRoomMats;
    public Mesh entrySubRoomMesh;
    public Material[] entrySubRoomMats;
    public List<Vector2Int> pendingShadowBake = new();
    // セルごとの構築状態。これらの参照は、セルが組み立てられる際に更新
    GameObject mainobj;//地面や壁などを統合したオブジェクト。当たり判定や頂点カラーの影をつけるコンポーネントをつける。
    GameObject propsParent;//蛍光灯などそれぞれ個別にコンポーネントをつける小物オブジェクトの親となるオブジェクト。
    GameObject subCellObj;//mainobjとは違い、ダクトなど当たり判定と頂点カラーによる影をつけないオブジェクト。
    // アクティブなセルおよびメッシュ結合バッファ
    public Dictionary<Vector2Int, ActiveCelldata> activeCellDatas = new Dictionary<Vector2Int, ActiveCelldata>();
    protected List<CombineInstance> subRoomMeshs = new List<CombineInstance>();
    protected List<Material> subRoomMats = new List<Material>();
    #region Unity setup and active-cell lifecycle
    void Awake()
    {
        Instance = this;
        mapGenerator = FindFirstObjectByType<BSPMapGenerator>();
        emptyObj = GameObject.Find("Empty");
    }

    public IEnumerator placeCellPrefab(Vector2Int cellKey,CellData cellData)
    {
        
        if(cellData == null)
        {
            cellData =  mapGenerator.getBaseCellData(cellKey);
        }
        if (cellData == null) yield break;
        entryMainRoomMesh = null;
        entryMainRoomMats = null;
        entrySubRoomMesh = null;
        entrySubRoomMats = null;
        subRoomMeshs.Clear();
        subRoomMats.Clear();
        
        InitializeMainRoomPrefab(cellData.cellPrefabData);
        InitializeSubRoom();
        //if(entryMainRoomMesh == null) yield break;
        RegisterObjPositions(cellData);
        
        

        activeCellDatas[cellKey] = new();

        Vector3 pos = new Vector3(cellKey.x * BSPMapGenerator.cellSize+BSPMapGenerator.cellSize/2, takasa, cellKey.y * BSPMapGenerator.cellSize+BSPMapGenerator.cellSize/2);
        mainobj = Instantiate(baseObject, pos, cellData.cellPrefabData.placeRotate, roomParent);
        mainobj.name = $"Cell_{cellKey.x}_{cellKey.y}";
        activeCellDatas[cellKey].mainObj = mainobj;
        activeCellDatas[cellKey].activeCellData = cellData;
        
        MeshFilter mf = mainobj.GetComponent<MeshFilter>();
        MeshRenderer mr = mainobj.GetComponent<MeshRenderer>();
        MeshCollider mc = mainobj.GetComponent<MeshCollider>();
        if (mf == null)mf = mainobj.AddComponent<MeshFilter>();
        if (mr == null)mr = mainobj.AddComponent<MeshRenderer>();
        if (mc == null)mc = mainobj.AddComponent<MeshCollider>();

        mf.sharedMesh = entryMainRoomMesh;
        mr.sharedMaterials = entryMainRoomMats ?? new Material[0];
        mc.sharedMesh = null;
        mc.sharedMesh = entryMainRoomMesh;
        
        propsParent = new GameObject($"propsParent");
        propsParent.transform.SetParent(mainobj.transform, false);
        
        subCellObj = new GameObject($"subCellObj");
        subCellObj.transform.SetParent(mainobj.transform, false);
        subCellObj.AddComponent<MeshFilter>().sharedMesh = entrySubRoomMesh;
        subCellObj.AddComponent<MeshRenderer>().sharedMaterials = entrySubRoomMats; 
       
        if (propsParent != null)
        {
            placeOtherObjects(cellData);  
            propsParent.transform.rotation = cellData.cellPrefabData.propsPositionRotate;    
        }     
    }
    
    public void DestroyCell(Vector2Int cellKey)
    {
        if(activeCellDatas.ContainsKey(cellKey))
        {
            Destroy(activeCellDatas[cellKey].mainObj);
            activeCellDatas.Remove(cellKey);
        }
    }


    #endregion

    #region Mesh assembly and placed objects
    public void InitializeMainRoomPrefab(CellPrefabData prefabData){
        if(prefabData.entryMainPrefab == null)return;
        
        // 変更: 宣言直後にタプル分解代入で即座に上書きされるだけの無意味な new() だったため初期化子を削除
        List<CombineInstance> _subRoomMeshs;
        Material[] _subRoomMats;
        (entryMainRoomMesh, entryMainRoomMats, _subRoomMeshs, _subRoomMats) = MeshCombiner.CombinePrefabMeshes(prefabData.entryMainPrefab, prefabData.combineTypeMap, prefabData.ChangeUVMap);
        if(_subRoomMeshs!=null){
            subRoomMeshs.AddRange(_subRoomMeshs);
            subRoomMats.AddRange(_subRoomMats);
        }
        //subRoommeshなどに追加する処理
    }


    public void InitializeSubRoom()
    {
        (entrySubRoomMesh, entrySubRoomMats) = MeshCombiner.CombineMeshesMaterials(subRoomMeshs, subRoomMats.ToArray());

        //subRoomObj.AddComponent<MeshFilter>().sharedMesh = finalMesh;
       // subRoomObj.AddComponent<MeshRenderer>().sharedMaterials = finalMats;        
    }
    protected void placeOtherObjects(CellData cellData)
    {
        var prefabTransform = cellData.cellPrefabData.entryMainPrefab.transform;
        var childTransforms = prefabTransform.GetComponentsInChildren<Transform>(true);
        var firstChildByName = new Dictionary<string, Transform>();

        // 名前ごとに最初の子だけを保持し、配置対象名ごとの全階層検索を避ける。
        foreach (Transform child in childTransforms)
        {
            if (!firstChildByName.ContainsKey(child.name))
                firstChildByName.Add(child.name, child);
        }

        var otherObjects = new List<otherObjectData>();
        foreach (string objectName in cellData.cellPrefabData.otherObjectNames)
        {
            if (!firstChildByName.TryGetValue(objectName, out Transform target))
                continue;

            otherObjects.Add(new otherObjectData
            {
                gameObject = target.gameObject,
                position = target.localPosition,
                rotation = target.rotation
            });
            cellData.cellPrefabData.combineTypeMap[objectName] = MeshCombiner.CombineType.NoCombine;
        }

        foreach (otherObjectData data in otherObjects)
        {
            GameObject obj = Instantiate(data.gameObject, propsParent.transform, false);
            obj.transform.localPosition = data.position;
            obj.transform.localRotation = data.rotation;
        }
    }
//randomPlaceObjectsという名前のオブジェクトの子オブジェクトをランダムで配置する
    protected void randomPlaceObjects(CellData cellData)
    {
         List<otherObjectData> otherObjects = new List<otherObjectData>();
        foreach (Transform child in cellData.cellPrefabData.entryMainPrefab.transform.GetComponentsInChildren<Transform>(true))
        {
            if (child.name =="randomPlaceObjects")
            {
                int index = 0;
                foreach (Transform child2 in child)
                {
                     index += 1;
                    Transform target = null;
                    int randomValue = Mathf.Abs(BSPMapGenerator.Hash(cellData.thiscellKey.x,cellData.thiscellKey.y, index, 1)) % 100;
                    if (randomValue < cellData.cellPrefabData.randomPlaceObjectChance)
                    {
                        target = child2;
                        // placementChance % の確率で入る
                    }
                    if (target != null)
                    {
                        otherObjects.Add
                        (
                            new otherObjectData
                            {
                                gameObject = target.gameObject,
                                position = target.localPosition,
                                rotation = target.rotation
                            }
                        );
                    }

                }

                break;
            }
        }
         foreach (otherObjectData data in otherObjects)
        {
            GameObject obj = Instantiate(data.gameObject,propsParent.transform,false);
            obj.transform.localPosition = data.position;
            obj.transform.localRotation = data.rotation;
        }
        

    }

    #endregion

    #region Prefab hierarchy and minimap helpers
    private void RegisterObjPositions(CellData cellData) 
    {
        GameObject prefab = cellData.cellPrefabData.entryMainPrefab;
        cellData.cellPrefabData.objPositions = new Dictionary<string, List<Transform>>();
        if (prefab == null) return; // prefabの中から「position」という名前のオブジェクトを探す 
        Transform position = FindChildRecursive(prefab.transform, "position");
        if (position == null) return;  
        // positionのすべての子孫を取得
        foreach (Transform child in position.GetComponentsInChildren<Transform>(true))
        { // position自身は除外 
            if (child == position) continue; 
            string objName = child.name; // 同じ名前のキーがなければ作成 
            if (!cellData.cellPrefabData.objPositions.TryGetValue(objName, out List<Transform> transforms))
            {
                transforms = new List<Transform>();
                cellData.cellPrefabData.objPositions.Add(objName, transforms);
            }
            transforms.Add(child); 
        }    
    } // 子孫を再帰的に検索して名前が一致するTransformを探す 
    private Transform FindChildRecursive(Transform parent, string targetName)
    { 
        foreach (Transform child in parent)
        { 
            if (child.name == targetName) return child; 
            Transform result = FindChildRecursive(child, targetName); 
            if (result != null) return result; 
        } 
        return null;    
    }

    #endregion
}