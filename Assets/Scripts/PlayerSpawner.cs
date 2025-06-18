using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner instance;

    private void Awake()
    {
        instance = this;
    }

    public GameObject playerPrefab;
    private GameObject player;

    public GameObject deathEffect;

    public float respawnTime = 5f;
    public Transform[] teamASpawns;
    public Transform[] teamBSpawns;

    // Store assigned team for respawning
    private Team assignedTeam;

    void Start()
    {
        if (PhotonNetwork.IsConnected)
        {
            SpawnPlayer();
        }
    }

    public void SpawnPlayer()
    {
        if (player == null)
        {
            assignedTeam = AssignTeamToLocalPlayer();
        }

        Transform spawnPoint = (assignedTeam == Team.TeamA) ?
            teamASpawns[Random.Range(0, teamASpawns.Length)] :
            teamBSpawns[Random.Range(0, teamBSpawns.Length)];

        player = PhotonNetwork.Instantiate(playerPrefab.name, spawnPoint.position, spawnPoint.rotation);

        player.GetComponent<PlayerController>().team = assignedTeam;

        MatchManager.teams[assignedTeam].Add(player);

        player.GetComponent<PhotonView>().RPC("SetTeam", RpcTarget.AllBuffered, (int)assignedTeam);
    }


    private Team AssignTeamToLocalPlayer()
    {
        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        // Even actor numbers go to TeamA, odd to TeamB
        Team assigned = (actorNumber % 2 == 0) ? Team.TeamB : Team.TeamA;

        // Save in custom properties
        ExitGames.Client.Photon.Hashtable myProps = new ExitGames.Client.Photon.Hashtable
    {
        { "Team", (int)assigned }
    };
        PhotonNetwork.LocalPlayer.SetCustomProperties(myProps);

        return assigned;
    }



    public void Die(string damager)
    {
        UIController.instance.deathText.text = "You were killed by " + damager;

        MatchManager.instance.UpdateStatsSend(PhotonNetwork.LocalPlayer.ActorNumber, 1, 1);

        if (player != null)
        {
            StartCoroutine(DieCo());
        }
    }

    public IEnumerator DieCo()
    {
        PhotonNetwork.Instantiate(deathEffect.name, player.transform.position, Quaternion.identity);

        PhotonNetwork.Destroy(player);
        player = null;
        UIController.instance.deathScreen.SetActive(true);

        yield return new WaitForSeconds(respawnTime);

        UIController.instance.deathScreen.SetActive(false);

        if (MatchManager.instance.state == MatchManager.GameState.Playing && player == null)
        {
            SpawnPlayer();
        }
    }
}
