using UnityEngine;
using System.Collections.Generic;

//-------------------------------VertexShadowBaker頂点カラーで影を作るスクリプト-------------------------------
//AO（Ambient Occlusion / 環境光遮蔽）とは、物体の凹んだ部分や他の物体と接近している部分に、周囲からの光が届きにくくなることで生じる影を近似的に計算する技術

//頂点カラーを表示できるカスタムシェーダーにより作成されたマテリアル。このコンポーネントがあるオブジェクトのみAO処理をする
//read/whilteがoffになっているプレハブは負荷

//各頂点を法線方向に押し出した点を中心として、
//法線に垂直な平面上に円を描き、その円周上の各点から
//円の中心（法線軸）に向かってレイを飛ばす。
//ヒットした面の法線との角度差で影の濃さを決める。

//コライダーによって当たり判定があるものしか判定しない。
//また、コライダーがあるが判定したくないオブジェクトはocclusionLayersによってレイヤー設定をし、レイヤーを分ける
//-----------------------------------------------------------------------------------------------------------
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VertexShadowBaker : MonoBehaviour
{
    [Tooltip("AO判定に使うレイヤーマスク")]
    public LayerMask occlusionLayers = ~0;

    [Header("AO")]
    [Range(0f, 1f)]public float aoStrength = 0.6f;//暗さの強度

    [Header("Color")]
    public Color brightColor = Color.white;
    public Color darkColor   = new Color(0f, 0f, 0f, 1f);

    [Header("Debug")]
    public bool applyOnStart   = true;
    public bool showGizmos     = false;

    private float normalOffset = 0.001f;//頂点を法線方向に押し出すオフセット（自己ヒット防止）
    private float circleRadius = 0.1f;//円の半径：レイの起点を法線軸からどれだけ離すか
    private int rayCount = 5;//円周上のレイの本数
    private float normalInfluence = 0.8f;//ヒット面の法線と自分の法線が同じ向きのとき影をどこまで薄くするか0：法線方向を無視（当たれば全部同じ濃さ）1：完全に同じ向きなら影なし（推奨）
    
    // Gizmo用データ
    struct RayData
    {
        public Vector3 origin;      // 円周上の起点
        public Vector3 end;         // レイの終端（ヒットなければ rayLength 先）
        public bool    didHit;
        public float   contribution;
    }
    private List<RayData> _gizmoRays = new List<RayData>();

    // Gizmo用円の中心と基底軸を頂点ごとに保存
    struct CircleData
    {
        public Vector3 center;
        public Vector3 tangentRight;
        public Vector3 tangentUp;
    }
    private List<CircleData> _gizmoCircles = new List<CircleData>();
    
    [ContextMenu("Bake")]
    //メッシュの各頂点についてAOを計算し、頂点カラーとして書き込む
    public void Bake()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null) return;
        if (mf.sharedMesh == null) return;
        
        Mesh mesh = Instantiate(mf.sharedMesh);
        mf.mesh = mesh;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals  = mesh.normals;
        Color[]   colors   = new Color[vertices.Length];

        _gizmoRays.Clear();
        _gizmoCircles.Clear();

        // 各頂点インデックスに所属する面法線を割り当てる
        int[] triangles = mesh.triangles;
        Vector3[] faceNormals = new Vector3[vertices.Length]; // デフォルトはゼロベクトル
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];

            Vector3 edge1    = vertices[i1] - vertices[i0];
            Vector3 edge2    = vertices[i2] - vertices[i0];
            Vector3 faceNorm = Vector3.Cross(edge1, edge2).normalized;

            // 各頂点に「まだ面法線が割り当てられていなければ」最初に見つかったものを使う
            if (faceNormals[i0] == Vector3.zero) faceNormals[i0] = faceNorm;
            if (faceNormals[i1] == Vector3.zero) faceNormals[i1] = faceNorm;
            if (faceNormals[i2] == Vector3.zero) faceNormals[i2] = faceNorm;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(vertices[i]);

            // normalInfluence用の法線：平均法線ではなく所属面の1つの法線を使う
            Vector3 faceNormalWorld = transform.TransformDirection(faceNormals[i]).normalized;

            float ao = ComputeAO(worldPos, faceNormalWorld);//自分の面と逆の面になるほど1に近づく。レイが当たらない場合は０
            colors[i] = Color.Lerp(brightColor, darkColor, ao * aoStrength);//aoの値をもとに頂点の色を決める
        }
        mesh.colors = colors;//各頂点の色を決定
    }

    //-----------------------AO計算------------------------
    //center：頂点を法線方向にnormalOffset押し出した点
    //centerを中心として法線と平行な半径circleRadiusの円を描く
    //円周上をrayCount等分した各点がrayOriginとしてcenterの方向へレイを飛ばす
    //レイが何かにヒットしたら向きの近さを求め、normalInfluenceの分だけ暗さを弱める
    //ヒットした時点でループをbreakし、以降のレイは判定しない
    //-----------------------------------------------------
    
    float ComputeAO(Vector3 worldPos, Vector3 worldNormal)
    {
        // 円の中心（法線オフセット済み）
        Vector3 center = worldPos + worldNormal * normalOffset;

        // 法線に直交する2軸を構築
        Vector3 helper = (Mathf.Abs(Vector3.Dot(worldNormal, Vector3.up)) < 0.99f)? Vector3.up : Vector3.forward;
        Vector3 tangentRight = Vector3.Cross(worldNormal, helper).normalized;
        Vector3 tangentUp = Vector3.Cross(tangentRight, worldNormal).normalized;

        // Gizmo 用に保存
        _gizmoCircles.Add(new CircleData
        {
            center       = center,
            tangentRight = tangentRight,
            tangentUp    = tangentUp,
        });

        float totalDark = 0f;

        for (int i = 0; i < rayCount; i++)
        {
            // 円周上の角度（等間隔）
            float angle  = 2f * Mathf.PI * i / rayCount;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            // 円周上の点
            Vector3 rayOrigin = center + (tangentRight * cos + tangentUp * sin) * circleRadius;

            // 方向：起点 → 中心（内向き収束）
            Vector3 rayDir = (center - rayOrigin).normalized;

            RaycastHit hitInfo;
            RayData rd = new RayData
            {
                origin = rayOrigin,
                end = rayOrigin + rayDir * circleRadius*2f,
            };

            if (Physics.Raycast(rayOrigin, rayDir, out hitInfo, circleRadius*2f, occlusionLayers))
            {
                rd.didHit = true;
                float dot = Vector3.Dot(worldNormal, hitInfo.normal);
                float normalFactor = 1f - Mathf.Max(0f, dot) * normalInfluence;
                totalDark = normalFactor;
                _gizmoRays.Add(rd);
                break;
            }
            _gizmoRays.Add(rd);
        }

        return totalDark;
    }

    // -------------------------------------------------------
    // Gizmo 可視化
    //   黄色ワイヤー円  = 各頂点の円形レイ配置
    //   緑〜赤の線      = レイ（contribution 0→1）
    // -------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        // 円のワイヤーフレーム
        if (_gizmoCircles != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            int seg = 32;
            foreach (CircleData cd in _gizmoCircles)
            {
                Vector3 prev = cd.center + cd.tangentRight * circleRadius;
                for (int s = 1; s <= seg; s++)
                {
                    float a = 2f * Mathf.PI * s / seg;
                    Vector3 next = cd.center + (cd.tangentRight * Mathf.Cos(a) +  cd.tangentUp    * Mathf.Sin(a)) * circleRadius;
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
            }
        }

        // レイ
        if (_gizmoRays != null)
        {
            foreach (RayData rd in _gizmoRays)
            {
                if (rd.didHit)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(rd.origin, rd.end);
                }
                else
                {
                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
                    Gizmos.DrawLine(rd.origin, rd.end);
                }
            }
        }
    }
}