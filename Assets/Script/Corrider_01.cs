using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Corrider_01 : Room
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
        int keikoutou_xnum = 1,keikoutou_znum = 1;

        if ((adjacentcellDatas[_neighborOffsets[0]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[0]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[0]] != true && (adjacentcellDatas[_neighborOffsets[0]].cellType == BSPMapGenerator.CellType.Corridor ))))
        && (adjacentcellDatas[_neighborOffsets[1]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[1]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[1]] != true && (adjacentcellDatas[_neighborOffsets[1]].cellType == BSPMapGenerator.CellType.Corridor ))))){
            keikoutou_xnum = 2;
        }
        else if ((adjacentcellDatas[_neighborOffsets[2]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[2]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[2]] != true && (adjacentcellDatas[_neighborOffsets[2]].cellType == BSPMapGenerator.CellType.Corridor ))))
        && (adjacentcellDatas[_neighborOffsets[3]] != null && (cellData.RoomConnectDirs == null 
        || cellData.RoomConnectDirs[_neighborOffsets[3]] == true
        || (cellData.RoomConnectDirs[_neighborOffsets[3]] != true && (adjacentcellDatas[_neighborOffsets[3]].cellType == BSPMapGenerator.CellType.Corridor ))))){
            keikoutou_znum = 2;
        }
       
        placeRoomKeikoutou(keikoutou_xnum, keikoutou_znum);          
    }
} 
