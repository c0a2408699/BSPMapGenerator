using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

//------------MeshCombiner複数のメッシュを統合するスクリプト------------
//使用できる関数
//CombinePrefabMeshes(),CombineMeshes(),CombineMeshesMaterials()
//それぞれ引数や返り値が違う
//-------------------------------------------------------------------

public static class MeshCombiner
{
    public enum CombineType
    {
        NormalCombine,
        NoCombine,
        return2ndMesh,
        rotate90,
        rotate180,
        rotate270
    }

    //デフォルトTypeMap
    public static readonly Dictionary<string, CombineType> DefaultCombineTypeMap =new Dictionary<string, CombineType>
    {
        { "wall-1", CombineType.NoCombine },
        { "wall-2", CombineType.NoCombine },
        { "wall-3", CombineType.NoCombine },
        { "wall-4", CombineType.NoCombine }
    };

    //------------CombinePrefabMeshes()-----------
    //引数：GameObjectやprefab、子オブジェクトの名前をキーにした処理種別マップ、子オブジェクトの名前をキーにしたUV設定マップ
    //返り値：(mesh, materials, combines2, materials2)
    //Prefab階層を走査し、同一Materialごとに統合。return2ndMeshは統合せずcombines2, materials2として返す
    //--------------------------------------------
    public static (Mesh mesh, Material[] materials, List<CombineInstance> combines2, Material[] materials2)CombinePrefabMeshes(GameObject prefab,Dictionary<string, CombineType> combineTypeMap = null,Dictionary<string, Vector2Int> changeUVMap = null)
    {
        if (prefab == null){
            Debug.LogError("CombinePrefabMeshes: prefab is null.");
            return EmptyPrefabResult();
        }

        combineTypeMap = combineTypeMap ?? DefaultCombineTypeMap;

        if (changeUVMap == null || changeUVMap.Count == 0) return BuildCombinedMesh(prefab, combineTypeMap);

        GameObject temporaryRoot = Object.Instantiate(prefab);
        temporaryRoot.hideFlags = HideFlags.HideAndDontSave;
        temporaryRoot.SetActive(false);

        try{
            foreach (KeyValuePair<string, Vector2Int> entry in changeUVMap)
                UVChanger.ChangeBoxUV(temporaryRoot, entry.Value, entry.Key);

            return BuildCombinedMesh(temporaryRoot, combineTypeMap);
        }
        finally{
            DestroyTemporary(temporaryRoot);
        }
    }

    //---------------CombineMeshes()--------------
    //有効な入力Meshをすべて1サブメッシュへ統合。Material情報は扱わず、meshのみを返す
    //--------------------------------------------
    public static Mesh CombineMeshes(IEnumerable<CombineInstance> combines)
    {
        if (combines == null){
            Debug.LogError("CombineMeshes: combines is null.");
            return new Mesh();
        }

        var validCombines = new List<CombineInstance>();
        foreach (CombineInstance combine in combines){
            if (combine.mesh != null)
                validCombines.Add(combine);
        }

        return BuildSingleSubMesh(validCombines);
    }

    //----------CombineMeshesMaterials()----------
    //入力ごとにサブメッシュを維持して統合し、Material配列を対応づけて返す
    //--------------------------------------------
    public static (Mesh mesh, Material[] materials)CombineMeshesMaterials(List<CombineInstance> combines, Material[] materials)
    {
        if (combines == null || combines.Count == 0) return (new Mesh(), new Material[0]);

        var validCombines = new List<CombineInstance>(combines.Count);
        var resultMaterials = new List<Material>(combines.Count);

        for (int i = 0; i < combines.Count; i++){
            CombineInstance combine = combines[i];
            if (combine.mesh == null)
                continue;

            validCombines.Add(combine);
            resultMaterials.Add(materials != null && i < materials.Length ? materials[i] : null);
        }

        if (validCombines.Count == 0) return (new Mesh(), new Material[0]);

        Mesh mesh = CreateMesh();
        mesh.CombineMeshes(validCombines.ToArray(), false, true);
        mesh.RecalculateBounds();
        return (mesh, resultMaterials.ToArray());
    }

    private static (Mesh mesh, Material[] materials, List<CombineInstance> combines2, Material[] materials2)BuildCombinedMesh(GameObject root, Dictionary<string, CombineType> combineTypeMap)
    {
        if (root == null){
            Debug.LogError("BuildCombinedMesh: root is null.");
            return EmptyPrefabResult();
        }

        var materialGroups = new Dictionary<Material, List<CombineInstance>>();
        var secondCombines = new List<CombineInstance>();
        var secondMaterials = new List<Material>();

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true)){
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;

            CombineDecision decision = ResolveCombineDecision(meshFilter.transform, root.transform, combineTypeMap);
            if (decision.skip) continue;

            Renderer renderer = meshFilter.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterials == null) continue;

            Mesh sourceMesh = meshFilter.sharedMesh;
            Material[] sourceMaterials = renderer.sharedMaterials;
            Matrix4x4 transform = root.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix * GetRotationMatrix(decision.rotation);

            int subMeshCount = Mathf.Min(sourceMesh.subMeshCount, sourceMaterials.Length);
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material material = sourceMaterials[subMeshIndex];
                if (material == null) continue;

                var combine = new CombineInstance{
                    mesh = sourceMesh,
                    subMeshIndex = subMeshIndex,
                    transform = transform
                };

                if (decision.isReturn2nd){
                    secondCombines.Add(combine);
                    secondMaterials.Add(material);
                    continue;
                }

                if (!materialGroups.TryGetValue(material, out List<CombineInstance> group)){
                    group = new List<CombineInstance>();
                    materialGroups.Add(material, group);
                }
                group.Add(combine);
            }
        }

        var (mesh, materials) = CombineGroupsToMesh(materialGroups);
        return (mesh, materials, secondCombines, secondMaterials.ToArray());
    }
    
    private static (Mesh mesh, Material[] materials)CombineGroupsToMesh(Dictionary<Material, List<CombineInstance>> materialGroups)
    {
        if (materialGroups.Count == 0) return (new Mesh(), new Material[0]);

        var groupCombines = new List<CombineInstance>(materialGroups.Count);
        var resultMaterials = new List<Material>(materialGroups.Count);
        var temporaryMeshes = new List<Mesh>(materialGroups.Count);

        try{
            foreach (KeyValuePair<Material, List<CombineInstance>> group in materialGroups){
                Mesh groupMesh = BuildSingleSubMesh(group.Value);
                temporaryMeshes.Add(groupMesh);
                groupCombines.Add(new CombineInstance { mesh = groupMesh, transform = Matrix4x4.identity });
                resultMaterials.Add(group.Key);
            }

            Mesh finalMesh = CreateMesh();
            finalMesh.CombineMeshes(groupCombines.ToArray(), false, false);
            finalMesh.RecalculateBounds();
            return (finalMesh, resultMaterials.ToArray());
        }
        finally{
            foreach (Mesh temporaryMesh in temporaryMeshes)
                DestroyTemporary(temporaryMesh);
        }
    }

    private static Mesh BuildSingleSubMesh(List<CombineInstance> combines)
    {
        Mesh mesh = CreateMesh();
        if (combines.Count > 0){
            mesh.CombineMeshes(combines.ToArray(), true, true);
            mesh.RecalculateBounds();
        }
        return mesh;
    }

    private static Mesh CreateMesh()
    {
        var mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        return mesh;
    }

    private static Matrix4x4 GetRotationMatrix(CombineType? rotation)
    {
        if (!rotation.HasValue) return Matrix4x4.identity;

        switch (rotation.Value)
        {
            case CombineType.rotate90:  return Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f));
            case CombineType.rotate180: return Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));
            case CombineType.rotate270: return Matrix4x4.Rotate(Quaternion.Euler(0f, 270f, 0f));
            default:                    return Matrix4x4.identity;
        }
    }

    private struct CombineDecision
    {
        public bool skip;
        public bool isReturn2nd;
        public CombineType? rotation;
    }

    private static CombineDecision ResolveCombineDecision(Transform target, Transform root, Dictionary<string, CombineType> combineTypeMap)
    {
        var decision = new CombineDecision();
        for (Transform current = target; current != null; current = current == root ? null : current.parent){
            if (!combineTypeMap.TryGetValue(current.name, out CombineType combineType)) continue;

            switch (combineType)
            {
                case CombineType.NoCombine:
                    decision.skip = true;
                    return decision;
                case CombineType.return2ndMesh:
                    decision.isReturn2nd = true;
                    break;
                case CombineType.rotate90:
                case CombineType.rotate180:
                case CombineType.rotate270:
                    if (!decision.rotation.HasValue)
                        decision.rotation = combineType;
                    break;
            }

        }
        return decision;
    }

    private static (Mesh mesh, Material[] materials, List<CombineInstance> combines2, Material[] materials2)EmptyPrefabResult()
    {
        return (new Mesh(), new Material[0], new List<CombineInstance>(), new Material[0]);
    }

    private static void DestroyTemporary(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(target);
        else
            Object.DestroyImmediate(target);
    }
}
