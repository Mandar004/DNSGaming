using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class MatchManager : MonoBehaviourPunCallbacks, IOnEventCallback
{
    public static MatchManager instance;
    public static Dictionary<Team, List<GameObject>> teams = new();
    private bool alreadyGameEnd = false;

    private void Awake()
    {
        instance = this;
        teams[Team.TeamA] = new List<GameObject>();
        teams[Team.TeamB] = new List<GameObject>();
    }

    public enum EventCodes : byte
    {
        NewPlayer,
        ListPlayers,
        UpdateStat,
        NextMatch,
        TimerSync
    }

    public List<PlayerInfo> allPlayers = new List<PlayerInfo>();
    private int index;

    private List<LeaderboardPlayer> lboardPlayers = new List<LeaderboardPlayer>();

    public enum GameState
    {
        Waiting,
        Playing,
        Ending
    }

    public int killsToWin = 3;
    public Transform mapCamPoint;
    public GameState state = GameState.Waiting;
    public float waitAfterEnding = 5f;

    public bool perpetual;

    public float matchLength = 180f;
    private float currentMatchTime;
    private float sendTimer;

    // Start is called before the first frame update
    void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            SceneManager.LoadScene(0);
        }
        else
        {
            NewPlayerSend(PhotonNetwork.NickName);

            state = GameState.Playing;

            SetupTimer();

            if (!PhotonNetwork.IsMasterClient)
            {
                UIController.instance.timerText.gameObject.SetActive(false);
            }
        }
    }

    public void AssignTeam(GameObject player)
    {
        int totalPlayers = teams[Team.TeamA].Count + teams[Team.TeamB].Count;

        Team assignedTeam = (totalPlayers % 2 == 0) ? Team.TeamA : Team.TeamB;

        player.GetComponent<PlayerController>().team = assignedTeam;
        teams[assignedTeam].Add(player);
    }

    // Update is called once per frame
    void Update()
    {
        if (state == GameState.Ending || currentMatchTime <= 0f)
        {

            if (!alreadyGameEnd)
            {
            ShowLeaderboard();

                EndGame();
            }


        }

//        if (PhotonNetwork.IsMasterClient)
        {
            if (currentMatchTime > 0f && state == GameState.Playing)
            {
                //Debug.Log("Check Bool " + alreadyGameEnd);
                currentMatchTime -= Time.deltaTime;

                if (currentMatchTime <= 0f)
                {
                    currentMatchTime = 0f;

                    state = GameState.Ending;

                    ListPlayersSend();

                    StateCheck();
                }

                UpdateTimerDisplay();

                sendTimer -= Time.deltaTime;
                if (sendTimer <= 0)
                {
                    sendTimer += 1f;

                    TimerSend();
                }
            }
            

        }
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code < 200)
        {
            EventCodes theEvent = (EventCodes)photonEvent.Code;
            object[] data = (object[])photonEvent.CustomData;

            //Debug.Log("Received event " + theEvent);

            switch (theEvent)
            {
                case EventCodes.NewPlayer:

                    NewPlayerReceive(data);

                    break;

                case EventCodes.ListPlayers:

                    ListPlayersReceive(data);

                    break;

                case EventCodes.UpdateStat:

                    UpdateStatsReceive(data);

                    break;

                case EventCodes.NextMatch:

                    NextMatchReceive();

                    break;

                case EventCodes.TimerSync:

                    TimerReceive(data);

                    break;
            }
        }
    }

    public override void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void NewPlayerSend(string username)
    {
        object[] package = new object[4];
        package[0] = username;
        package[1] = PhotonNetwork.LocalPlayer.ActorNumber;
        package[2] = 0;
        package[3] = 0;


        PhotonNetwork.RaiseEvent(
            (byte)EventCodes.NewPlayer,
            package,
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
            new SendOptions { Reliability = true }
            );
    }

    public void NewPlayerReceive(object[] dataReceived)
    {
        PlayerInfo player = new PlayerInfo((string)dataReceived[0], (int)dataReceived[1], (int)dataReceived[2], (int)dataReceived[3]);

        allPlayers.Add(player);

        ListPlayersSend();
    }

    public void ListPlayersSend()
    {
        object[] package = new object[allPlayers.Count + 1];

        package[0] = state;

        for (int i = 0; i < allPlayers.Count; i++)
        {
            object[] piece = new object[4];

            piece[0] = allPlayers[i].name;
            piece[1] = allPlayers[i].actor;
            piece[2] = allPlayers[i].kills;
            piece[3] = allPlayers[i].deaths;

            package[i + 1] = piece;
        }

        PhotonNetwork.RaiseEvent(
            (byte)EventCodes.ListPlayers,
            package,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            new SendOptions { Reliability = true }
            );
    }

    public void ListPlayersReceive(object[] dataReceived)
    {
        allPlayers.Clear();

        state = (GameState)dataReceived[0];

        for (int i = 1; i < dataReceived.Length; i++)
        {
            object[] piece = (object[])dataReceived[i];

            PlayerInfo player = new PlayerInfo(
                (string)piece[0],
                (int)piece[1],
                (int)piece[2],
                (int)piece[3]
                );

            allPlayers.Add(player);

            if (PhotonNetwork.LocalPlayer.ActorNumber == player.actor)
            {
                index = i - 1;
            }
        }

        StateCheck();
        Debug.Log("ListPlayersReceive");
    }

    public void UpdateStatsSend(int actorSending, int statToUpdate, int amountToChange)
    {
        object[] package = new object[] { actorSending, statToUpdate, amountToChange };

        PhotonNetwork.RaiseEvent(
            (byte)EventCodes.UpdateStat,
            package,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            new SendOptions { Reliability = true }
            );
    }

    public void UpdateStatsReceive(object[] dataReceived)
    {
        Debug.Log("UpdateStatsReceive");
        int actor = (int)dataReceived[0];
        int statType = (int)dataReceived[1];
        int amount = (int)dataReceived[2];

        for (int i = 0; i < allPlayers.Count; i++)
        {
            if (allPlayers[i].actor == actor)
            {
                switch (statType)
                {
                    case 0: //kills
                        allPlayers[i].kills += amount;
                        Debug.Log("Player " + allPlayers[i].name + " : kills " + allPlayers[i].kills);
                        break;

                    case 1: //deaths
                        allPlayers[i].deaths += amount;
                        Debug.Log("Player " + allPlayers[i].name + " : deaths " + allPlayers[i].deaths);
                        break;
                }

                if (i == index)
                {
                    UpdateStatsDisplay();
                }

                if (UIController.instance.leaderboard.activeInHierarchy)
                {
                    ShowLeaderboard();
                }

                break;
            }
        }

        ScoreCheck();
    }

    public void UpdateStatsDisplay()
    {
        if (allPlayers.Count > index)
        {

            UIController.instance.killsText.text = "Kills: " + allPlayers[index].kills;
            UIController.instance.deathsText.text = "Deaths: " + allPlayers[index].deaths;
        }
        else
        {
            UIController.instance.killsText.text = "Kills: 0";
            UIController.instance.deathsText.text = "Deaths: 0";
        }
    }
    void ShowLeaderboard(string winnerName = "")
    {
        UIController.instance.leaderboard.SetActive(true);

        foreach (LeaderboardPlayer lp in lboardPlayers)
        {
            Destroy(lp.gameObject);
        }
        lboardPlayers.Clear();

        UIController.instance.leaderboardPlayerDisplay.gameObject.SetActive(false);

        List<PlayerInfo> sorted = SortPlayers(allPlayers);

        if (!string.IsNullOrEmpty(winnerName))
        {
            LeaderboardPlayer winnerDisplay = Instantiate(UIController.instance.leaderboardPlayerDisplay, UIController.instance.leaderboardPlayerDisplay.transform.parent);
            winnerDisplay.SetDetails(winnerName, 0, 0); // No kills/deaths, just a message
            winnerDisplay.gameObject.SetActive(true);
            lboardPlayers.Add(winnerDisplay);
        }

        foreach (PlayerInfo player in sorted)
        {
            LeaderboardPlayer newPlayerDisplay = Instantiate(UIController.instance.leaderboardPlayerDisplay, UIController.instance.leaderboardPlayerDisplay.transform.parent);
            newPlayerDisplay.SetDetails(player.name, player.kills, player.deaths);
            newPlayerDisplay.gameObject.SetActive(true);
            lboardPlayers.Add(newPlayerDisplay);
        }
    }

    /* void ShowLeaderboard()
     {
         UIController.instance.leaderboard.SetActive(true);

         foreach(LeaderboardPlayer lp in lboardPlayers)
         {
             Destroy(lp.gameObject);
         }
         lboardPlayers.Clear();

         UIController.instance.leaderboardPlayerDisplay.gameObject.SetActive(false);

         List<PlayerInfo> sorted = SortPlayers(allPlayers);

         foreach(PlayerInfo player in sorted)
         {
             LeaderboardPlayer newPlayerDisplay = Instantiate(UIController.instance.leaderboardPlayerDisplay, UIController.instance.leaderboardPlayerDisplay.transform.parent);

             newPlayerDisplay.SetDetails(player.name, player.kills, player.deaths);

             newPlayerDisplay.gameObject.SetActive(true);

             lboardPlayers.Add(newPlayerDisplay);
         }
     }*/

    private List<PlayerInfo> SortPlayers(List<PlayerInfo> players)
    {
        List<PlayerInfo> sorted = new List<PlayerInfo>();

        while (sorted.Count < players.Count)
        {
            int highest = -1;
            PlayerInfo selectedPlayer = players[0];

            foreach (PlayerInfo player in players)
            {
                if (!sorted.Contains(player))
                {
                    if (player.kills > highest)
                    {
                        selectedPlayer = player;
                        highest = player.kills;
                    }
                }
            }

            sorted.Add(selectedPlayer);
        }

        return sorted;
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();

        SceneManager.LoadScene(0);
    }

    private int GetTeamKills(Team team)
    {
        int totalKills = 0;

        foreach (PlayerInfo player in allPlayers)
        {
            GameObject playerObj = teams[team].Find(obj => obj.GetPhotonView().Owner.ActorNumber == player.actor);
            if (playerObj != null)
            {
                totalKills += player.kills;
            }
        }

        return totalKills;
    }
    bool winnerFound = false;
    string winningTeamName = "";
    void ScoreCheck()
    {
        int teamAKills = GetTeamKills(Team.TeamA);
        int teamBKills = GetTeamKills(Team.TeamB);

        Debug.Log("killsToWin   " + killsToWin);
        Debug.Log("teamAKills " + teamAKills);
        Debug.Log("teamBKills  " + teamBKills);

        if (teamAKills >= killsToWin)
        {
            winnerFound = true;
            winningTeamName = "Team A Wins!";
        }
        else if (teamBKills >= killsToWin)
        {
            winnerFound = true;
            winningTeamName = "Team B Wins!";
        }
        else if (currentMatchTime <= 0f)
        {
            winnerFound = true; // Mark round as over
            if (teamAKills > teamBKills)
                winningTeamName = "Team A Wins!";
            else if (teamBKills > teamAKills)
                winningTeamName = "Team B Wins!";
            else
                winningTeamName = "Round Over!";
        }

        if (winnerFound)
        {
            if (/*PhotonNetwork.IsMasterClient &&*/ state != GameState.Ending)
            {
                state = GameState.Ending;
                ListPlayersSend();
            }

            UIController.instance.endText.text = winningTeamName;
             photonView.RPC("RPC_ShowWinningTeam", RpcTarget.All, winningTeamName);
            Debug.Log("ScoreCheck---" + UIController.instance.endText.text);
        }
    }
  

    /* void ScoreCheck()
     {
         bool winnerFound = false;

         foreach(PlayerInfo player in allPlayers)
         {
             if(player.kills >= killsToWin && killsToWin > 0)
             {
                 winnerFound = true;
                 break;
             }
         }

         if(winnerFound)
         {
             if(PhotonNetwork.IsMasterClient && state != GameState.Ending)
             {
                 state = GameState.Ending;
                 ListPlayersSend();
             }
         }
     }*/

    void StateCheck()
    {
        if (state == GameState.Ending)
        {
            Debug.Log("StateCheck");
           
                EndGame();
            Debug.Log("Check Bool " + alreadyGameEnd);
            
        }
    }

    void EndGame()
    {
        if (!alreadyGameEnd)
        {
            alreadyGameEnd = true;
            state = GameState.Ending;

            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.DestroyAll();
            }

            UIController.instance.endScreen.SetActive(true);
            ShowLeaderboard();
            // SetWinningTeamText();
            ScoreCheck();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Camera.main.transform.position = mapCamPoint.position;
            Camera.main.transform.rotation = mapCamPoint.rotation;

            StartCoroutine(EndCo());
        }


    }
    IEnumerator ExitGame()
    {
        yield return new WaitForSeconds(20f);

        PhotonNetwork.LeaveRoom();
        UIController.instance.ReturnToMainMenu();
    }
    void SetWinningTeamText()
    {
        int teamAKills = 0;
        int teamBKills = 0;

        foreach (PlayerInfo player in allPlayers)
        {
            // Safely check for Team A
            if (MatchManager.teams[Team.TeamA].Exists(go =>
                go != null &&
                go.GetComponent<PlayerController>() != null &&
                go.GetComponent<PlayerController>().photonView.Owner.ActorNumber == player.actor))
            {
                teamAKills += player.kills;
            }
            // Safely check for Team B
            else if (MatchManager.teams[Team.TeamB].Exists(go =>
                go != null &&
                go.GetComponent<PlayerController>() != null &&
                go.GetComponent<PlayerController>().photonView.Owner.ActorNumber == player.actor))
            {
                teamBKills += player.kills;
            }
        }

        Debug.Log($"Team A kills {teamAKills}  Team B Kills {teamBKills}");

        string winningText = "";
        if (teamAKills > teamBKills)
            winningText = "Team A Wins!";
        else if (teamBKills > teamAKills)
            winningText = "Team B Wins!";
        else
            winningText = "Round Over!";


        UIController.instance.endText.text = winningText;
        alreadyGameEnd = true;


    }


    private IEnumerator EndCo()
    {
        yield return new WaitForSeconds(waitAfterEnding);

        if (!perpetual)
        {
            PhotonNetwork.AutomaticallySyncScene = false;
            PhotonNetwork.LeaveRoom();
        }
        else
        {

            Debug.Log("EndCO else loop");
           
            /*if(PhotonNetwork.IsMasterClient)
            {
                if (!Launcher.instance.changeMapBetweenRounds)
                {
                    NextMatchSend();
                } else
                {
                    int newLevel = Random.Range(0, Launcher.instance.allMaps.Length);

                    if(Launcher.instance.allMaps[newLevel] == SceneManager.GetActiveScene().name)
                    {
                        NextMatchSend();
                    } else
                    {
                        PhotonNetwork.LoadLevel(Launcher.instance.allMaps[newLevel]);
                    }
                }
            }*/
        }
    }

    public void NextMatchSend()
    {
        PhotonNetwork.RaiseEvent(
            (byte)EventCodes.NextMatch,
            null,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            new SendOptions { Reliability = true }
            );
    }

    public void NextMatchReceive()
    {
        state = GameState.Playing;

        UIController.instance.endScreen.SetActive(false);
        UIController.instance.leaderboard.SetActive(false);

        foreach (PlayerInfo player in allPlayers)
        {
            player.kills = 0;
            player.deaths = 0;
        }

        UpdateStatsDisplay();

        PlayerSpawner.instance.SpawnPlayer();

        SetupTimer();
    }

    public void SetupTimer()
    {
        if (matchLength > 0)
        {
            currentMatchTime = matchLength;
            UpdateTimerDisplay();
        }
    }

    public void UpdateTimerDisplay()
    {

        var timeToDisplay = System.TimeSpan.FromSeconds(currentMatchTime);

        UIController.instance.timerText.text = timeToDisplay.Minutes.ToString("00") + ":" + timeToDisplay.Seconds.ToString("00");
    }

    public void TimerSend()
    {
        object[] package = new object[] { (int)currentMatchTime, state };

        PhotonNetwork.RaiseEvent(
            (byte)EventCodes.TimerSync,
            package,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            new SendOptions { Reliability = true }
            );
    }

    public void TimerReceive(object[] dataReceived)
    {
        currentMatchTime = (int)dataReceived[0];
        state = (GameState)dataReceived[1];

        UpdateTimerDisplay();

        UIController.instance.timerText.gameObject.SetActive(true);
    }
}

[System.Serializable]
public class PlayerInfo
{
    public string name;
    public int actor, kills, deaths;

    public PlayerInfo(string _name, int _actor, int _kills, int _deaths)
    {
        name = _name;
        actor = _actor;
        kills = _kills;
        deaths = _deaths;
    }
}
