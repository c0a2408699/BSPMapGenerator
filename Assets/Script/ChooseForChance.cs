using System;
using System.Collections.Generic;
using UnityEngine;

//-----------------ChooseForChunce加重ランダム選択-----------------
//引数
//選択するものリストと加重の変数
//seed：シード
//useIndices：選対象を絞り込みたい場合、対象にしたいlistのインデックスのみのリスト(省略可能)
//返り値
//引数のlist内のインデックスを返す
//----------------------------------------------------------------

public static class ChooseForChunce
{

    public static int GetRandomIndex<T>(List<T> list, Func<T, int> chance, int seed, List<int> useIndices = null)
    {
        System.Random rng = new System.Random(seed);

        // useIndicesがnullの場合、list内のすべてを使用する
        bool useAll = useIndices == null || useIndices.Count == 0;

        int total = 0;//重みの合計

        if (!useAll)//重みの合計を求める
        {
            foreach (int index in useIndices)
            {
                if (index >= 0 && index < list.Count)
                    total += chance(list[index]);
            }
        }
        else
        {
            foreach (T item in list)
                total += chance(item);
        }

        if (total <= 0)
            return -1;

        //累積分布による抽選
        int randomPoint = rng.Next(0, total);
        int cumulative = 0;

        if (!useAll)
        {
            foreach (int index in useIndices)
            {
                if (index < 0 || index >= list.Count)
                    continue;

                cumulative += chance(list[index]);

                if (randomPoint < cumulative)
                    return index;
            }
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                cumulative += chance(list[i]);

                if (randomPoint < cumulative)
                    return i;
            }
        }

        return -1;
    }
}