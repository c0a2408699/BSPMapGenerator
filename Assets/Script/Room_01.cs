using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Room_01 : Room
{
    public override void Init()
    {
        base.Init();
        StartCoroutine(InitCoroutine());
    }

    private IEnumerator InitCoroutine()//フレームを分けて負荷を分散
    {
        yield return null; 
        Init1();
        yield return null;   
        InitializeMainRoomPrefab();
        yield return null;   
        //placePipe();
        yield return null;  
        InitializeSubRoom();
        yield return null;
        shadowBakeReady = true;
    }

    public void Init1()
    {
        PlacePrefab = entryPrefabMap[entryPrefabType.Normal][0].prefab;
        
        if(PlacePrefab == emptyObj)return;

        addNoCombineWall_ForAdjacentRooms();
       
        placeRoomKeikoutou(2,2);          
    }
} 
