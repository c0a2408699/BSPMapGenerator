using UnityEngine;
using System.Collections.Generic;

public class KeikoutouManager : MonoBehaviour
{
    public GameObject keikoutou01;
    private Quaternion rot = Quaternion.identity;
    public Material targetMaterial;
    public float keikoutou_takasa = 3.25f;
    public bool dengen = true;
    private bool dengen_old = true;
    List<GameObject> keikoutouList = new List<GameObject>();

    void Update()
    {
        if(dengen_old != dengen)
        {
            foreach (var obj in keikoutouList)
            {
                if (obj == null) continue;
                foreach (var l in obj.GetComponentsInChildren<Light>(true))
                {
                    l.enabled = dengen;
                }
                var mrs =obj.GetComponentsInChildren<MeshRenderer>();
                foreach(var mr in mrs)
                {
                    foreach (var mat in mr.sharedMaterials)
                    {
                        Debug.Log(mat.name + " / " + targetMaterial.name);
                        mat.DisableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", Color.black);
                        if(mat == targetMaterial)
                        {
                            Debug.Log(mat.name + " / " + targetMaterial.name);
                            
                        }
                    }
                }
            }
        }
       dengen_old = dengen;
        
    }
    public void placeKeikoutou(BSPMapGenerator.prefab_type prefabType,Transform roomtransform,int keikoutou_x_num,int keikoutou_z_num, int xcellsize = -1,int zcellsize = -1)
    {
        if (xcellsize < 0) xcellsize = BSPMapGenerator.cellSize;
        if (zcellsize < 0) zcellsize = BSPMapGenerator.cellSize;
        float x_offset = xcellsize / (float)(keikoutou_x_num)/2;
        float z_offset = zcellsize / (float)(keikoutou_z_num)/2;
        float x_step = xcellsize / (float)(keikoutou_x_num);
        float z_step = zcellsize / (float)(keikoutou_z_num);

        for (int x = 1; x <= keikoutou_x_num; x++)
        {
            for (int z = 1; z <= keikoutou_z_num; z++)
            {
                Vector3 localPos = new Vector3(
                    (-xcellsize / 2f) + x_offset + x_step * (x-1),
                    keikoutou_takasa,
                    (-zcellsize / 2f) + z_offset + z_step * (z-1)
                );

                var obj = Instantiate(keikoutou01, roomtransform.position + localPos, rot, roomtransform);
                keikoutouList.Add(obj);
            }
        }
    }
}
