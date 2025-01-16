using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class Spawner : MonoBehaviour
{
    /// <summary>
    /// - Spawner 클래스
    /// 싱글턴으로 선언된 스포너는 인게임의 플레이어 스폰과 위치 갱신
    /// </summary>
    public static Spawner instance;
    private HashSet<string> currentUsers = new HashSet<string>();
    
    void Awake() {
        instance = this;
    }

    public void Spawn(LocationUpdate data) {
        if (!GameManager.instance.isLive) {
            return;
        }
        
        HashSet<string> newUsers = new HashSet<string>();

        foreach(LocationUpdate.UserLocation user in data.users) {
            newUsers.Add(user.id);

            Debug.Log($"Processing user: {user.id}, x: {user.x}, y: {user.y}");
            GameObject player = GameManager.instance.pool.Get(user);

            if (player == null)
            {
                Debug.LogError($"Player object is null for user: {user.id}");
            }
            else if (player.GetComponent<PlayerPrefab>() == null)
            {
                Debug.LogError($"PlayerPrefab component is missing for user: {user.id}");
            }
            PlayerPrefab playerScript = player.GetComponent<PlayerPrefab>();

            Debug.Log($"Updating position for user: {user.id}, x: {user.x}, y: {user.y}");
            playerScript.UpdatePosition(user.x, user.y);
        }

        foreach (string userId in currentUsers) {
            if (!newUsers.Contains(userId)) {
                GameManager.instance.pool.Remove(userId);
            }
        }
        
        currentUsers = newUsers;
    }
}