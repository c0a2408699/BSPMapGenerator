using UnityEngine;

//-------------UVChangerUVを書き換える-------------
//prefab内の指定名オブジェクトのUVを書き換える
//ChangeBoxUV：タイル状のテクスチャの特定マスへ書き換える
// Textureは正方形のみに対応
//----------------------------------------------------------------------------

    

public static class UVChanger
{
    public static void ChangeBoxUV(GameObject targetObj, Vector2Int uvOffset, string changeUVObj = "UVtarget", int divisionNum = 8)
    {
        Transform target = FindChildRecursive(targetObj.transform, changeUVObj);
        if (target == null)
        {
            Debug.LogWarning($"{changeUVObj} が見つかりませんでした");
            return;
        }

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter == null) return;

        Mesh mesh = meshFilter.mesh;
        Vector2[] uvs = mesh.uv;
        float cellSize = 1f / divisionNum;

        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = new Vector2(
                (uvs[i].x / divisionNum) + uvOffset.x * cellSize,
                (uvs[i].y / divisionNum) + uvOffset.y * cellSize
            );
        }
        mesh.uv = uvs;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform result = FindChildRecursive(child, name);
            if (result != null) return result;
        }
        return null;
    }
}
