using System;
using System.Collections.Generic;
using UnityEngine;

//-----------------RandomSelect加重ランダム選択-----------------
//引数
//選択するものリストと加重の変数
//seed：シード
//useIndices：選対象を絞り込みたい場合、対象にしたいlistのインデックスのみのリスト(省略可能)
//返り値
//引数のlist内のインデックスを返す
//----------------------------------------------------------------

public static class RandomSelect
{
    public static int GetRandomIndex<T>(List<T> list, Func<T, int> chance, int seed, List<int> useIndices = null)
    {
        System.Random rng = new System.Random(seed);

        // useIndicesがnullまたは空の場合、list内のすべてを使用する
        bool useAll = useIndices == null || useIndices.Count == 0;

        int total = 0; // 重みの合計

        if (!useAll)
        {
            foreach (int index in useIndices)
            {
                if (index >= 0 && index < list.Count)
                {
                    total += (chance == null) ? 1 : chance(list[index]);
                }
            }
        }
        else
        {
            foreach (T item in list)
            {
                total += (chance == null) ? 1 : chance(item);
            }
        }

        if (total <= 0)
            return 0;

        // 累積分布による抽選
        int randomPoint = rng.Next(0, total);
        int cumulative = 0;

        if (!useAll)
        {
            foreach (int index in useIndices)
            {
                if (index < 0 || index >= list.Count)
                    continue;

                cumulative += (chance == null) ? 1 : chance(list[index]);

                if (randomPoint < cumulative)
                    return index;
            }
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                cumulative += (chance == null) ? 1 : chance(list[i]);

                if (randomPoint < cumulative)
                    return i;
            }
        }

        return 0;
    }
    public static Vector2Int getRandomVector2(Vector2Int startVector,Vector2Int endVector, int seed = 0){
       System.Random random = (seed == 0)? new System.Random() :new System.Random(seed); // シード固定

        int minX = Mathf.Min(startVector.x, endVector.x);
        int maxX = Mathf.Max(startVector.x, endVector.x);
        int minY = Mathf.Min(startVector.y, endVector.y);
        int maxY = Mathf.Max(startVector.y, endVector.y);

        return new Vector2Int(random.Next(minX, maxX + 1),random.Next(minY, maxY + 1));
    }
}