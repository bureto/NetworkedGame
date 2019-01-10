using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ENet;
using System;

// NOTE: channel ids for sending packets goes as such:
//  0 for login stuff and initialization, stuff that can wait
//  1 for lobby functions, where latency is kinda important
//  2 for in game functions, important
//  3 for movement since we are sending alot of packets

public class serverTest : MonoBehaviour
{
    public static int maps = 2;
    public static int gamemodes = 2;
    public static int lobbies = 350;

    public class LobbyAndChallenges
    {
        // first dimension is map, 2nd gametype, 3rd is game #. for id, 4th dimension is players in lobby
        //public game[,,] game = new game[maps,gamemodes,maxLobbies];
        public int[,,] gameSeed = new int[maps, gamemodes, lobbies];
        public bool[,,] gameStarted = new bool[maps, gamemodes, lobbies];
        public string[,,] gameName = new string[maps,gamemodes, lobbies];
        //private string[,,] gameOwner = new string[maps, gamemodes, maxLobbies];
        private string[,,] gamePass = new string[maps, gamemodes, lobbies];
        public byte[,,] slots = new byte[maps, gamemodes, lobbies]; //number of players connected - 1, for example if 1 player connected slots = 0
        public int[,,,] id = new int[maps, gamemodes, lobbies, 4]; // index of client in RemoteClient array
        public bool[,,,] loaded = new bool[maps, gamemodes, lobbies, 4];
        public bool[,,,] dead = new bool[maps, gamemodes, lobbies, 4];
        public bool[,,,] ready = new bool[maps, gamemodes, lobbies, 4];
        public int[,,,] distance = new int[maps, gamemodes, lobbies, 4];
        public IEnumerator[,,] SendMovement = new IEnumerator[maps, gamemodes, lobbies];
        public IEnumerator[,,] matchTimer = new IEnumerator[maps, gamemodes, lobbies];
        public int[,,] matchTime = new int[maps, gamemodes, lobbies];

        private static int maxChallenges = 5000;

        private string[,] recordHolder = new string[maps, maxChallenges];
        private string[,] creator = new string[maps, maxChallenges];
        private int[,] timesPlayed = new int[maps, maxChallenges];
        private int[,] cDistance = new int[maps, maxChallenges];

        public bool LobbyNull(byte map, byte gamemode, int num)
        {
            if(gameName[map,gamemode,num] == null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool GameHasFinished(byte map, byte mode, int num)
        {
            for(int i = 0; i <= slots[map,mode,num]; i++)
            {
                if (!dead[map, mode, num, i])
                    return false;
            }
            return true;
        }

        public bool AllPlayersReady(byte map, byte mode, int num)
        {
            for (int i = 0; i < ((mode + 1) * 2); i++)
            {
                if (!ready[map, mode, num, i])
                    return false;
            }
            return true;
        }

        public void ReturnGame(byte map, byte gamemode, int num, out string gameName, out string pass, out byte slots)
        {
            gameName = this.gameName[map,gamemode,num];
            pass = this.gamePass[map, gamemode, num];
            slots = this.slots[map,gamemode,num];
        }

        public void ReturnChallenges(byte map, int num, out string recordHolder, out string creator, out int timesPlayed, out int distance)
        {
            recordHolder = this.recordHolder[map,num];
            creator = this.creator[map,num];
            timesPlayed = this.timesPlayed[map,num];
            distance = this.cDistance[map,num];
        }

        public int CreateLobby(int peerID, byte map, byte gamemode, string clientName, string name, string pass)
        {
            // find first available lobby 'slot'
            for(int i = 0; i < lobbies; i++)
            {
                if(gameName[map,gamemode,i] == null)
                {
                    int seed = UnityEngine.Random.Range(12, 8123051);
                    gameName[map,gamemode, i] = name;
                    gamePass[map,gamemode, i] = pass;
                    id[map, gamemode, i, 0] = peerID;
                    Debug.Log("assigned player to " + map + ", " + gamemode + ", " + i + ", " + 0 + " proof: his id in that spot: " + id[map,gamemode, i, 0]);
                    slots[map, gamemode, i] = 0;
                    return i;
                }
            }
            throw new System.ArgumentException("Could not create lobby: too many lobbies on this server");
        }

        public void StartGame(byte map, byte gamemode, int num)
        {
            matchTime[map, gamemode, num] = 0;
            gameSeed[map,gamemode, num] = UnityEngine.Random.Range(12, 8123051);
            for(int i = 0; i <= slots[map,gamemode,num]; i++)
            {
                loaded[map, gamemode, num, i] = false;
                dead[map, gamemode, num, i] = false;
                ready[map, gamemode, num, i] = false;
                distance[map, gamemode, num, i] = 0;
            }

        }

        public bool FindFirstAvailableLobby(int peerID, byte map, out byte gamemode, out int num)
        {
            for (gamemode = 0; gamemode < 2; gamemode++)
            {
                for (num = 0; num < lobbies; num++)
                {
                    if (gameName[map, gamemode, num] != null)
                    {
                        if (!gameStarted[map, gamemode, num])
                        {
                            if(slots[map,gamemode,num] < ((gamemode + 1) *2) - 1)
                            if (gamePass[map, gamemode, num] == "")
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            num = 0;
            return false;
        }

        public bool FindLobbyAssociatedWithClient(int id, out byte m, out byte g, out int n, out byte p)
        {
            for(m = 0; m < maps; m++)
            {
                for (g = 0; g < gamemodes; g++)
                {
                    for (n = 0; n < lobbies; n++)
                    {
                        for (p = 0; p <= slots[m,g,n]; p++)
                        {
                            if (id == this.id[m, g, n, p])
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            m = 0; g = 0; n = 0; p = 0;
            return false;
        }
   
        public void LeaveLobby(byte map, byte gamemode, int num, byte pos)
        {
            // shift all slots above this pos down one
            int slots = this.slots[map, gamemode, num];
            for (int i = pos; i < slots; i++) // make all slots between pos-onelessfrommaxslots equal to one above;
            {
                int posAbove = i + 1;
                id[map, gamemode, num, i] = id[map, gamemode, num, posAbove];
                loaded[map, gamemode, num, i] = loaded[map, gamemode, num, posAbove];
                dead[map, gamemode, num, i] = dead[map, gamemode, num, posAbove];
                ready[map, gamemode, num, i] = ready[map, gamemode, num, posAbove];
                distance[map, gamemode, num, i] = distance[map, gamemode, num, posAbove];
                
            }

            id[map, gamemode, num, slots] = -1; // -1 = null since lowest assigned client id is 1 (always make last slot empty since we shift down)
            loaded[map, gamemode, num, slots] = false;
            dead[map, gamemode, num, slots] = false;
            ready[map, gamemode, num, slots] = false;
            distance[map, gamemode, num, slots] = 0;


            Debug.Log(slots);
            // delete player from simulation
            //Destroy(game[map, gamemode, num].transform.Find("Boulders").GetChild(pos));

            // if no players in lobby, delete it
            if (slots < 1)
            {
                gameName[map, gamemode, num] = null;
                //Destroy(game[map, gamemode, num].transform);
                gameStarted[map, gamemode, num] = false;
            }
            else
                this.slots[map, gamemode, num] -= 1;
        }
    }

    public LobbyAndChallenges lac = new LobbyAndChallenges();

    Host server;
    Address address;
    ushort port = 40000;
    static int maxClients = 4000;
    int playerCount;

    private ServerHandleData[] shd = new ServerHandleData[maxClients];
    public DataBaseHandler DBH;
    [NonSerialized]
    public Peer[] peers = new Peer[maxClients];

    #region player values
    [NonSerialized]
    public bool[] playerInLobby = new bool[maxClients];
    [NonSerialized]
    public string[] playerName = new string[maxClients];
    private int[] xp = new int[maxClients];
    private byte[] skin = new byte[maxClients];
    private byte[] powerup = new byte[maxClients];
    private List<byte>[] skins = new List<byte>[maxClients];
    private List<byte>[] powerups = new List<byte>[maxClients];
    private int[] rocks = new int[maxClients];
    private int[] boulderTokens = new int[maxClients];
    private int[] timePlayed = new int[maxClients];
    private int[] soloWins = new int[maxClients];
    private int[] fourPlayerWins = new int[maxClients];
    private Vector3[] playerPosition = new Vector3[maxClients];
    private Vector3[] playerRotation = new Vector3[maxClients];

    int[] skinPrices = {0, 800, 2000 };
    int[] powerupPrices = {0, 1500, 3000, 1500};
    int[] tokenRockPrices = { 2000};
    int[] tokenRockQuantities = { 400 };

    #endregion

    // Use this for initialization
    void Start()
    {
        for (byte m = 0; m < maps; m++)
        {
            for (byte g = 0; g < gamemodes; g++)
            {
                for (int l = 0; l < lobbies; l++)
                {
                    lac.SendMovement[m,g,l] = SendMovement(m, g, l);
                    lac.matchTimer[m, g, l] = MatchTimer(m, g, l);
                    for (int p = 0; p < 4; p++)
                    {
                        lac.id[m, g, l, p] = -1;
                    }
                }
            }
        }
        ENet.Library.Initialize();
        StartServer();
        DBH = new DataBaseHandler();

        for(int i = 0; i < maxClients; i++)
        {
            skins[i] = new List<byte>();
            powerups[i] = new List<byte>();
        }
    }

    private void StartServer()
    {
        server = new Host();
        address = new Address();
        address.Port = port;
        server.Create(address, maxClients, 4, 16000000, 8000000);
    }

    private void Update()
    {
        ENet.Event netEvent;
        server.Service(0, out netEvent);
        switch (netEvent.Type)
        {
            case ENet.EventType.None:
                break;
            case ENet.EventType.Connect:
                Debug.Log("Client connected (ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ")");
                playerCount++;
                for (int i = 0; i < maxClients; i++)
                {
                    if (!peers[i].IsSet || peers[i].Data == (IntPtr)(-1))
                    {
                        peers[i] = netEvent.Peer;
                        peers[i].Data = new System.IntPtr(i);
                        shd[i] = new ServerHandleData();
                        shd[i].Initialize(this, netEvent.Peer.IP);
                        Debug.Log(i);
                        break;
                    }
                }
                Debug.Log("assigned peer");
                break;

            case ENet.EventType.Disconnect:
                Debug.Log("Client disconnected (ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ")");
                int peerID = (int)netEvent.Peer.Data;
                Debug.Log(peerID);
                peers[peerID].Data = (IntPtr)(-1);
                playerName[peerID] = null;
                // logout player from db
                shd[peerID].ClientDisconnected(xp[peerID], rocks[peerID], boulderTokens[peerID], skin[peerID], powerup[peerID]);
                shd[peerID] = null;
                //find lobby associated with client
                byte map;
                byte gamemode;
                int num;
                byte pos;
                if (lac.FindLobbyAssociatedWithClient(peerID, out map, out gamemode, out num, out pos))
                {
                    // subtract slot from lobby
                    playerInLobby[peerID] = false;
                    lac.LeaveLobby(map, gamemode, num, pos);

                    // tell other clients that we left
                    SendToOthersLeftLobby(map, gamemode, num, pos);
                    CheckIfGameDone(map,gamemode,num);
                }
                break;

            case ENet.EventType.Timeout:
                Debug.Log("Client timeout (ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ")");
                peerID = (int)netEvent.Peer.Data;
                peers[peerID].Data = (IntPtr)(-1);
                playerName[peerID] = null;
                // logout player from db
                shd[peerID].ClientDisconnected(xp[peerID], rocks[peerID], boulderTokens[peerID], skin[peerID], powerup[peerID]);

                //find lobby associated with client
                if (lac.FindLobbyAssociatedWithClient(peerID, out map, out gamemode, out num, out pos))
                {
                    // subtract slot from lobby
                    Debug.Log("still going");
                    playerInLobby[peerID] = false;
                    lac.LeaveLobby(map, gamemode, num, pos);

                    // tell other clients that we left
                    SendToOthersLeftLobby(map, gamemode, num, pos);
                    CheckIfGameDone(map, gamemode, num);
                }
                break;

            case ENet.EventType.Receive:
                byte[] data = ByteBuffer.GetByteBuffer(); //new byte[netEvent.Packet.Length];
                netEvent.Packet.CopyTo(data);
                shd[(int)netEvent.Peer.Data].HandleNetworkMessages(data, (int)netEvent.Peer.Data);

                netEvent.Packet.Dispose();
                break;
        }
    }

    IEnumerator StopSendingMovement (byte map, byte mode, int num)
    {
        yield return new WaitForSeconds(5);
        StopCoroutine(lac.SendMovement[map, mode, num]);
    }

    IEnumerator MatchTimer(byte map, byte mode, int num)
    {
        while (true)
        {
            yield return new WaitForSeconds(1);
            lac.matchTime[map, mode, num]++;
        }
    }

    IEnumerator SendMovement(byte map, byte mode, int num)
    {
        while (true)
        {
            ByteBuffer1 buffer4 = new ByteBuffer1();
            buffer4.WriteByte(2);
            buffer4.WriteByte(lac.slots[map, mode, num]);
            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                buffer4.WriteVector3(playerPosition[lac.id[map, mode, num, i]]); 
                buffer4.WriteVector3(playerRotation[lac.id[map, mode, num, i]]);
            }
            byte[] data = buffer4.ToArray();
            Packet packet = new Packet();
            packet.Create(data, data.Length, PacketFlags.Unsequenced);
            buffer4.Dispose();
            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                peers[lac.id[map, mode, num, i]].Send(3, ref packet);
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    public void LoginRegisterResponse(int peerID, byte response) // 1=registered, 2=badlogin, 3=toomanyloginattempts, 4=toomanyconnections, 5=alreadyLoggedIn, 6=tooManyRegistrations, 7=spammingRegistrations
    {
        Debug.Log("response = " + response);
        ByteBuffer1 buffer = new ByteBuffer1();
        buffer.WriteByte(8);
        buffer.WriteByte(response);
        Packet packet = new Packet();
        byte[] data = buffer.ToArray();
        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        
        buffer.Dispose();
    }

    public void LoginSuccess(int peerID, string name, int xp, int rocks, int boulderTokens, int timePlayed, int soloWins, int fourPlayerWins, List<byte> skins, List<byte> powerups, byte skin, byte powerup)
    {
        LoadAllLobbies(peerID);
        playerName[peerID] = name;
        this.xp[peerID] = xp;
        this.rocks[peerID] = rocks;
        this.boulderTokens[peerID] = boulderTokens;
        this.timePlayed[peerID] = timePlayed;
        this.soloWins[peerID] = soloWins;
        this.fourPlayerWins[peerID] = fourPlayerWins;
        this.skin[peerID] = skin;
        this.skins[peerID] = skins;
        this.powerups[peerID] = powerups;
        this.powerup[peerID] = powerup;


        ByteBuffer1 buffer = new ByteBuffer1();
        buffer.WriteByte(7);
        buffer.WriteByte(0);
        buffer.WriteString(name);
        buffer.WriteInteger(xp);
        buffer.WriteInteger(rocks);
        buffer.WriteInteger(boulderTokens);
        buffer.WriteInteger(timePlayed);
        buffer.WriteInteger(soloWins);
        buffer.WriteInteger(fourPlayerWins);

        buffer.WriteByte((byte)skins.Count);
        buffer.WriteByte((byte)powerups.Count);
        buffer.WriteBytes(skins.ToArray());
        buffer.WriteBytes(powerups.ToArray());
        buffer.WriteByte(skin);
        buffer.WriteByte(powerup);
        Packet packet = new Packet();
        byte[] data = buffer.ToArray();
        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        buffer.Dispose();      
    }

    // for newly created accs or for guests
    public void LoginSuccess(int peerID, string name, byte guest)
    {
        LoadAllLobbies(peerID);

        playerName[peerID] = name;
        this.xp[peerID] = 1000;
        this.rocks[peerID] = 0;
        this.boulderTokens[peerID] = 0;
        this.timePlayed[peerID] = 0;
        this.soloWins[peerID] = 0;
        this.fourPlayerWins[peerID] = 0;
        this.skin[peerID] = 0;
        this.skins[peerID].Clear();this.skins[peerID].Add(0);
        this.powerups[peerID].Clear();this.powerups[peerID].Add(0);
        this.powerup[peerID] = 0;

        ByteBuffer1 buffer = new ByteBuffer1();
        buffer.WriteByte(7);
        buffer.WriteByte(guest); // 2 = newly registered ,since 0 = login, and 1 = guest
        buffer.WriteString(name);
        Packet packet = new Packet();
        byte[] data = buffer.ToArray();
        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        buffer.Dispose();
    }

    public void AllowLoginRegister(int peerID)
    {
        ByteBuffer1 buffer = new ByteBuffer1();
        buffer.WriteByte(9);
        Packet packet = new Packet();
        byte[] data = buffer.ToArray();
        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        
        buffer.Dispose();
    }

    public void QuickPlay(int peerID, byte map)
    {
        if(!playerInLobby[peerID])
        {
            byte gamemode;
            int num;

            if(lac.FindFirstAvailableLobby(peerID, map, out gamemode, out num))
            {
                ConnectLobby(peerID, map, gamemode, num, "");
            }
            else
            {
                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteByte(23);
                Debug.Log("map = " + map);
                buffer.WriteByte(map);
                Packet packet = new Packet();
                byte[] data = buffer.ToArray();
                packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                peers[peerID].Send(1, ref packet);
                buffer.Dispose();
                
            }
        }
    }


    public void ConnectLobby(int peerID, byte map, byte gamemode, int num, string pass)
    {
        if (!playerInLobby[peerID])
        {
            if (!lac.LobbyNull(map, gamemode, num))
            {
                if(!lac.gameStarted[map,gamemode,num])
                { 
                    string gameName;
                    string gamePass;
                    byte slots;
                    lac.ReturnGame(map, gamemode, num, out gameName, out gamePass, out slots);
                    Debug.Log(peerID);
                    // if game isnt full
                    if (slots < ((gamemode + 1) * 2) - 1) 
                    {
                        if (pass == gamePass)
                        {
                            playerInLobby[peerID] = true;
                            //assign player to slot
                            slots++;
                            lac.slots[map, gamemode, num] = slots;
                            lac.id[map, gamemode, num, slots] = peerID;


                            // tell client it successfully connected to lobby
                            ByteBuffer1 buffer2 = new ByteBuffer1();
                            buffer2.WriteByte(10);
                            buffer2.WriteByte(map);
                            buffer2.WriteByte(gamemode);
                            buffer2.WriteInteger(num);
                            buffer2.WriteString(gameName);
                            buffer2.WriteByte(slots);
                            Packet packet2 = new Packet();
                            byte[] data2 = buffer2.ToArray();
                            packet2.Create(data2, data2.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                            peers[peerID].Send(1, ref packet2);

                            buffer2.Dispose();

                            UpdateLobbySlots(map, gamemode, num, slots);

                            
                            // tell all clients that player connected(below)
                            ByteBuffer1 buffer = new ByteBuffer1();
                            buffer.WriteByte(12);
                            buffer.WriteString(playerName[peerID]);
                            buffer.WriteInteger(xp[peerID]);
                            //buffer.WriteByte(skin[peerID]);
                            Debug.Log("slots = " + slots);
                            buffer.WriteByte(slots);
                            Packet packet = new Packet();
                            byte[] data = buffer.ToArray();
                            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);

                            for (byte i = 0; i <= slots; i++)
                            {
                                int loopPeerID = lac.id[map, gamemode, num, i];
                                Debug.Log(peerID);

                                if (i != slots)
                                {
                                    // tell each client in lobby about new client                       
                                    peers[loopPeerID].Send(1, ref packet);

                                    // tell new client information about each client in lobby
                                    ByteBuffer1 buffer1 = new ByteBuffer1();
                                    buffer1.WriteByte(12);
                                    buffer1.WriteString(playerName[loopPeerID]);
                                    Debug.Log(playerName[loopPeerID]);
                                    buffer1.WriteInteger(xp[loopPeerID]);
                                    //buffer1.WriteByte(skin[loopPeerID]);
                                    buffer1.WriteByte(i);
                                    Packet packet1 = new Packet();
                                    byte[] data1 = buffer1.ToArray();
                                    packet1.Create(data1, data1.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                                    peers[peerID].Send(1, ref packet1);

                                    buffer1.Dispose();
                                }

                            }
                            buffer.Dispose();
                            if (slots == (2 * (gamemode + 1)) - 1)
                            {
                                SendStartGame(map, gamemode, num);
                            }
                        }
                        else
                        {
                            /// pass wrong
                            ByteBuffer1 buffer = new ByteBuffer1();
                            buffer.WriteByte(24);
                            Packet packet = new Packet();
                            byte[] data = buffer.ToArray();
                            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                            peers[peerID].Send(1, ref packet);
                            buffer.Dispose();
                        }
                    }
                    else
                    {
                        // lobby full
                        ByteBuffer1 buffer = new ByteBuffer1();
                        buffer.WriteByte(19);
                        buffer.WriteByte(2);
                        Packet packet = new Packet();
                        byte[] data = buffer.ToArray();
                        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                        peers[peerID].Send(1, ref packet);
                        buffer.Dispose();
                    }
                }
                else
                {
                    // game started
                    ByteBuffer1 buffer = new ByteBuffer1();
                    buffer.WriteByte(19);
                    buffer.WriteByte(1);
                    Packet packet = new Packet();
                    byte[] data = buffer.ToArray();
                    packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                    peers[peerID].Send(1, ref packet);
                    buffer.Dispose();
                        
                }
            }
            else
            {
                // lobby doesnt exist
                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteByte(19);
                buffer.WriteByte(0);
                Packet packet = new Packet();
                byte[] data = buffer.ToArray();
                packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                peers[peerID].Send(1, ref packet);
                buffer.Dispose();
            }
        }
           
    }

    void SendStartGame(byte map, byte gamemode, int num)
    {
        lac.StartGame(map, gamemode, num);
        StartCoroutine(lac.SendMovement[map, gamemode, num]); // start timer
        // tell clients in lobby to start game
        ByteBuffer1 buffer4 = new ByteBuffer1();
        buffer4.WriteByte(3);
        buffer4.WriteInteger(lac.gameSeed[map, gamemode, num]);

        buffer4.WriteByte(lac.slots[map, gamemode, num]);
        for (int i = 0; i <= lac.slots[map, gamemode, num]; i++)
        {
            buffer4.WriteByte(skin[lac.id[map, gamemode, num, i]]);        // write players skins

            // zero player at start so it wont go to his last game position
            playerPosition[lac.id[map, gamemode, num, i]] = Vector3.zero; 
            playerRotation[lac.id[map, gamemode, num, i]] = Vector3.zero;
        }
       

        Packet packet4 = new Packet();
        byte[] data4 = buffer4.ToArray();
        packet4.Create(data4, data4.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);

        for (byte i = 0; i <= lac.slots[map,gamemode,num]; i++)
        {
            int peerID = lac.id[map, gamemode, num, i];
            peers[peerID].Send(1, ref packet4);
        }

        // updates lobby list on clients to tell em game started and cant be joined
        ByteBuffer1 buffer1 = new ByteBuffer1();
        buffer1.WriteByte(26);
        buffer1.WriteByte(map);
        buffer1.WriteByte(gamemode);
        buffer1.WriteInteger(num);
        buffer1.WriteByte(1);
        byte[] data1 = buffer1.ToArray();
        SendPacketToAllClientsLoggedIn(data1, PacketFlags.Reliable | PacketFlags.Unsequenced, 1); 
        lac.gameStarted[map, gamemode, num] = true;
    }


    public void ReadyUp(int peerID, byte map, byte gamemode, int num, byte pos)
    {
        if (lac.id[map, gamemode, num, pos] == peerID) // verifies pos in lobby
        {
            lac.ready[map, gamemode, num, pos] = true;     
            
            // notify clients in lobby that player readied
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteByte(27);
            buffer.WriteByte(pos);
            Packet packet = new Packet();
            byte[] data = buffer.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            for(int i = 0; i <= lac.slots[map, gamemode, num]; i++)
            {
                peers[lac.id[map,gamemode,num,i]].Send(1, ref packet);
            }
            buffer.Dispose();

            // start game if all ready
            if (lac.AllPlayersReady(map,gamemode,num))
            {
                SendStartGame(map, gamemode, num);
            }
        }
    }

    public void LeaveLobby(int peerID, byte map, byte gamemode, int num, byte pos)
    {
        if (lac.id[map, gamemode, num, pos] == peerID) // verifies pos in lobby
        {
            // subtracts one from slots, opening up a slot for another client
            lac.LeaveLobby(map, gamemode, num, pos);
            playerInLobby[peerID] = false;
            // tell other clients that we left
            SendToOthersLeftLobby(map, gamemode, num, pos);
            // notify client that he succeffully left lobby
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteByte(13);
            Packet packet = new Packet();
            byte[] data = buffer.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            peers[peerID].Send(1, ref packet);
            Debug.Log("player " + playerName[peerID] + " left");
            buffer.Dispose();
            CheckIfGameDone(map, gamemode, num);
        }
    }

    public void KickPlayer(int peerID, byte map, byte gamemode, int num, byte pos)
    {
        Debug.Log("sent kick");
        if (lac.id[map, gamemode, num, 0] == peerID) // checks to see if it is lobby owner who is sending packet
        {
            Debug.Log("sent kick");
            int kickedPlayerID = lac.id[map, gamemode, num, pos];
            // subtracts one from slots, opening up a slot for another client
            lac.LeaveLobby(map, gamemode, num, pos);
            playerInLobby[kickedPlayerID] = false;

            // tell other clients that he left
            SendToOthersLeftLobby(map, gamemode, num, pos);

            // notify client that he was kicked from lobby
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteByte(18);
            Packet packet = new Packet();
            byte[] data = buffer.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            peers[kickedPlayerID].Send(1, ref packet);
            
            buffer.Dispose();
            Debug.Log("sent kick");
        }

    }

    public void CreateLobby(int peerID, byte map, byte gamemode, string name, string pass)
    {
        if (!playerInLobby[peerID])
        {
            byte Protected = 0;
            // if no name was set, create default name
            if (name == "")
            {
                name = playerName[peerID] + "'s game";
            }
            // if it is password protected
            if (pass != "")
            {
                Protected = 1;
            }

            int i = lac.CreateLobby(peerID, map, gamemode, playerName[peerID], name, pass); //creates lobby and returns lobby id

            // tell client he successfully created lobby
            playerInLobby[peerID] = true;
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteByte(10);
            buffer.WriteByte(map);
            buffer.WriteByte(gamemode);
            buffer.WriteInteger(i);
            buffer.WriteString(name);
            buffer.WriteByte(0);
            //buffer.WriteInteger(gamemode);
            Packet packet = new Packet();
            byte[] data = buffer.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            peers[peerID].Send(1, ref packet);

            // update clients with new lobby info
            ByteBuffer1 buffer1 = new ByteBuffer1();
            buffer1.WriteByte(16);
            buffer1.WriteByte(map);
            buffer1.WriteByte(gamemode);
            buffer1.WriteInteger(i);
            buffer1.WriteString(name);
            buffer1.WriteString(playerName[peerID]);
            buffer1.WriteByte(Protected); // tell clients that it is password protected
            buffer1.WriteByte(0); // tell clients that game not started
            buffer1.WriteByte(0); // one person in lobby (owner)
            byte[] data1 = buffer1.ToArray();
            SendPacketToAllClientsLoggedIn(data1, PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
            buffer.Dispose();
            buffer1.Dispose();
            
        }
    }

    public void CreateChallenge(int peerID, byte map, int seed)
    {
        ByteBuffer1 buffer = new ByteBuffer1();
        buffer.WriteByte(14);
        buffer.WriteByte(map);
        buffer.WriteInteger(seed);
        Packet packet = new Packet();
        packet.Create(buffer.ToArray());
        peers[peerID].Send(1, ref packet);
        buffer.Dispose();
    }

    public void LoadAllLobbies(int peerID)
    {
        for (byte m = 0; m < maps; m++)
        {
            for (byte g = 0; g < gamemodes; g++)
            {
                for (int n = 0; n < lobbies; n++)
                {
                    string gameName;
                    string gamePass;
                    byte slots;
                    byte Protected = 0;
                    // if lobby is not null
                    if (!lac.LobbyNull(m, g, n))
                    {
                        lac.ReturnGame(m, g, n, out gameName, out gamePass, out slots);

                        if (gamePass != "")
                        {
                            Protected = 1;
                        }
                        // update client with new lobby info
                        ByteBuffer1 buffer1 = new ByteBuffer1();
                        buffer1.WriteByte(16);
                        buffer1.WriteByte(m);
                        buffer1.WriteByte(g);
                        buffer1.WriteInteger(n);
                        buffer1.WriteString(gameName);
                        buffer1.WriteString(playerName[lac.id[m,g,n,0]]);
                        buffer1.WriteByte(Protected); // tell clients that it is password protected
                        buffer1.WriteByte(Convert.ToByte(lac.gameStarted[m, g, n]));
                        buffer1.WriteByte(slots);
                        Packet packet = new Packet();
                        byte[] data = buffer1.ToArray();
                        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                        peers[peerID].Send(0, ref packet);
                        buffer1.Dispose();                        
                    }
                }
            }
        }
    }

    public void OpenGate(int peerID)
    {
        byte map;
        byte mode;
        int lobby;
        byte slot;
        if (lac.FindLobbyAssociatedWithClient(peerID, out map, out mode, out lobby, out slot))
        {
            // set player has loaded
            lac.loaded[map, mode, lobby, slot] = true;
            // tell other players he has loaded
            ByteBuffer1 buffer1 = new ByteBuffer1();
            buffer1.WriteByte(11);
            buffer1.WriteByte(slot);
            byte[] data1 = buffer1.ToArray();
            buffer1.Dispose();
            Packet packet1 = new Packet();
            packet1.Create(data1, data1.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            for (int i = 0; i <= lac.slots[map, mode, lobby]; i++)
            {
                peers[lac.id[map, mode, lobby, i]].Send(2, ref packet1);
            }


            if (lac.slots[map, mode, lobby] > 0) // prevents player from playing online solo match
            {
                // check if all players in lobby are loaded
                for (int n = 0; n <= lac.slots[map, mode, lobby]; n++)
                {
                    // if player has not loaded
                    if (lac.loaded[map, mode, lobby, n] == false)
                    {
                        Debug.Log("player " + n + " is not ready");
                        return;
                    }
                    Debug.Log("player " + n + " is ready");
                }

                // open gate on all clients if all players are loaded

                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteByte(6);
                Packet packet = new Packet();
                byte[] data = buffer.ToArray();
                packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);

                for (int i = 0; i <= lac.slots[map, mode, lobby]; i++)
                {
                    Debug.Log("sent open gate");
                    peers[lac.id[map, mode, lobby, i]].Send(2, ref packet);
                    lac.loaded[map, mode, lobby, i] = false; // prevents this method from being activated when not supposed to
                }
                buffer.Dispose();
                StartCoroutine(lac.matchTimer[map, mode, lobby]);
            }           
        }
    }


    public void UpdatePlayerPosition(int peerID, Vector3 position, Vector3 rotation)
    {
        playerPosition[peerID] = position;
        playerRotation[peerID] = rotation;     
    }
    /*
    public void SendPlayerPosition(int peerID, byte map, byte mode, int num, byte pos, Vector3 position, Vector3 rotation)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            ByteBuffer1 buffer4 = new ByteBuffer1();
            buffer4.WriteByte(2);
            buffer4.WriteByte(pos);
            buffer4.WriteVector3(position); // informs client how many player's positions he should read
            buffer4.WriteVector3(rotation);
            Packet packet = new Packet();
            packet.Create(buffer4.ToArray());

            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                    peers[lac.id[map, mode, num, i]].Send(3, ref packet);
            }
            buffer4.Dispose();           
        }
    }*/

    public void SendExplode(int peerID, byte map, byte mode, int num, byte pos)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            ByteBuffer1 buffer4 = new ByteBuffer1();
            buffer4.WriteByte(20);
            buffer4.WriteByte(pos);
            Packet packet = new Packet();
            byte[] data = buffer4.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);

            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                {
                    Debug.Log("sent dead, pos = " + pos + ", sending to client: " + i);
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
                }
            }
        }
    }

    public void SendPowerup(int peerID, byte map, byte mode, int num, byte pos)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            ByteBuffer1 buffer4 = new ByteBuffer1();
            buffer4.WriteByte(31);
            buffer4.WriteByte(pos);
            buffer4.WriteByte(powerup[peerID]);
            Packet packet = new Packet();
            byte[] data = buffer4.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);

            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                {
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
                }
            }
        }
    }

    public void Dead(int peerID, byte map, byte mode, int num, byte pos, int distance)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            lac.dead[map, mode, num, pos] = true;
            lac.distance[map, mode, num, pos] = distance;
            CheckIfGameDone(map, mode, num);
        }
    }

    public void CheckIfGameDone(byte map, byte mode, int num)
    {
        if (lac.gameStarted[map, mode, num])
        {
            if (lac.GameHasFinished(map, mode, num))
            {
                lac.gameStarted[map, mode, num] = false;
                StopCoroutine(lac.matchTimer[map, mode, num]);
                StartCoroutine(StopSendingMovement(map, mode, num));
                // sort scores and give players who stayed xp accordingly
                int[] descendingDistances = new int[lac.slots[map, mode, num] + 1];
                for (int i = 0; i <= lac.slots[map, mode, num]; i++)
                {
                    descendingDistances[i] = lac.distance[map, mode, num, i];
                }
                Array.Sort(descendingDistances);
                Array.Reverse(descendingDistances);

                int firstPlacePlayerPos = 0;

                for (int i = 0; i <= lac.slots[map, mode, num]; i++)
                {
                    for (int j = 0; j <= lac.slots[map, mode, num]; j++)
                    {
                        if (lac.distance[map, mode, num, i] == descendingDistances[j])
                        {
                            int scoreConstant = mode + 2 - j;
                            if (scoreConstant <= 0)
                                scoreConstant = 1;

                            if (j == 0) // do stuff for first place player
                            {
                                firstPlacePlayerPos = i; // save reference so we can send it to clients later
                                if (mode == 0)
                                    soloWins[lac.id[map, mode, num, i]]++;
                                else if (mode == 1)
                                    fourPlayerWins[lac.id[map, mode, num, i]]++;                                  
                            }

                            xp[lac.id[map, mode, num, i]] += (scoreConstant * lac.matchTime[map, mode, num]);
                            rocks[lac.id[map, mode, num, i]] += Mathf.RoundToInt(lac.matchTime[map, mode, num] * 1.38f);
                            timePlayed[lac.id[map, mode, num, i]] += lac.matchTime[map, mode, num];
                            CheckForUnlocks(lac.id[map, mode, num, i]);
                            Debug.Log("match time" + lac.matchTime[map, mode, num]);
                        }
                    }
                }

                // (updates game list) tell clients that the game has finished 
                ByteBuffer1 buffer1 = new ByteBuffer1();
                buffer1.WriteByte(26);
                buffer1.WriteByte(map);
                buffer1.WriteByte(mode);
                buffer1.WriteInteger(num);
                buffer1.WriteByte(0);
                byte[] data1 = buffer1.ToArray();
                SendPacketToAllClientsLoggedIn(data1, PacketFlags.Reliable | PacketFlags.Unsequenced, 1);


                // tells clients in lobby game is done
                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteByte(28);
                if (lac.slots[map, mode, num] < (((mode + 1) * 2) - 1)) 
                    buffer.WriteByte(0); // not enough players
                else
                    buffer.WriteByte(1); // enough players
                buffer.WriteInteger(lac.matchTime[map, mode, num]);
                buffer.WriteByte((byte)firstPlacePlayerPos);
                byte[] data = buffer.ToArray();
                Packet packet = new Packet();
                packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                for (int i = 0; i <= lac.slots[map, mode, num]; i++)
                {
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
                }
                
            }
        }
    }

    public void SendObstacleHit(int peerID, byte map, byte mode, int num, byte pos, byte type, string name, float dmg)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            ByteBuffer1 buffer4 = new ByteBuffer1();
            buffer4.WriteByte(21);
            buffer4.WriteByte(type);
            buffer4.WriteString(name);
            buffer4.WriteFLoat(dmg);
            Packet packet = new Packet();
            byte[] data = buffer4.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable);

            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
            }
            
            buffer4.Dispose();
        }
    }

    //-------------------------------
    // for server authoritative movement
    public void MovePlayers(int peerID, byte map, byte mode, int num, byte pos, byte input)
    {
        if(lac.id[map,mode,num,pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            //lac.game[map, mode, num].transform.Find("Boulders").GetChild(pos).GetComponent<movePlayer>().input = input; 
        }
    }
    public void SendMovePlayers(byte map, byte gamemode, int num, Vector3[] position)
    {
        ByteBuffer1 buffer4 = new ByteBuffer1();
        buffer4.WriteInteger(2);
        buffer4.WriteInteger(position.Length); // informs client how many player's positions he should read
        int i = 0;
        foreach(Vector3 player in position)
        {
            buffer4.WriteVector3(position[i]);
            i++;
        }
        Packet packet = new Packet();
        packet.Create(buffer4.ToArray());

        for (i = 0; i <= lac.slots[map,gamemode,num]; i++)
        {
            peers[lac.id[map,gamemode,num,i]].Send(0,ref packet);
        }
        buffer4.Dispose();      
    }

    //-------------------------------------


    public void PurchaseItem(int peerID, byte itemType, byte itemID)
    {
        if (itemType == 0)
        {
            if (boulderTokens[peerID] >= skinPrices[itemID])
            {
                if (!SkinOwned(peerID, itemID))
                {
                    skins[peerID].Add(itemID);
                    shd[peerID].UnlockSkin(itemID);
                    boulderTokens[peerID] -= skinPrices[itemID];
                }
                else return;
            }
            else return;
        }
        else if(itemType == 1)
        {
            if (rocks[peerID] >= powerupPrices[itemID])
            {
                if (!PowerupOwned(peerID, itemID))
                {
                    powerups[peerID].Add(itemID);
                    shd[peerID].UnlockPowerup(itemID);
                    rocks[peerID] -= powerupPrices[itemID];
                }
                else return;
            }
            else return;
        }
        else if (itemType == 2)
        {
            if (rocks[peerID] >= tokenRockPrices[itemID])
            {
                boulderTokens[peerID] += tokenRockQuantities[itemID];
                rocks[peerID] -= tokenRockPrices[itemID];
            }
            else return;
        }

        ByteBuffer1 buffer = new ByteBuffer1();
        buffer.WriteByte(29);
        buffer.WriteByte(1);
        buffer.WriteByte(itemType);
        buffer.WriteByte(itemID);
        Packet packet = new Packet();
        byte[] data = buffer.ToArray();
        packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(1, ref packet);
    }


    public void EquipItem(int peerID, byte itemType, byte itemID)
    {

        if (itemType == 0)
        {

            if (SkinOwned(peerID, itemID)) // if he does
            {
                skin[peerID] = itemID; // update player to new skin
                // tell each client he changed skin
                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteByte(30);
                buffer.WriteByte(itemType);
                buffer.WriteByte(itemID);
                Packet packet = new Packet();
                byte[] data = buffer.ToArray();
                packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                peers[peerID].Send(1, ref packet);
            }        
        }
        else if(itemType == 1)
        {
            // Check to see if player has powerup

            if (PowerupOwned(peerID, itemID)) // if he does
            {
                powerup[peerID] = itemID; // simply save the equiped item so when player exits game we can save it in database

                // tell each client he changed powerup
                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteByte(30);
                buffer.WriteByte(itemType);
                buffer.WriteByte(itemID);
                Packet packet = new Packet();
                byte[] data = buffer.ToArray();
                packet.Create(data, data.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                peers[peerID].Send(1, ref packet);
            }
            
        }
    }


    // notify all clients in lobby that player has left
    public void SendToOthersLeftLobby(byte map, byte gamemode, int num, byte pos)
    {
        byte slots = lac.slots[map, gamemode, num];

        ByteBuffer1 buffer1 = new ByteBuffer1();
        // lobby is empty, tell client to delete it
        if (lac.gameName[map,gamemode,num] == null)
        {
            buffer1.WriteByte(25);
            buffer1.WriteByte(map);
            buffer1.WriteByte(gamemode);
            buffer1.WriteInteger(num);
            byte[] data = buffer1.ToArray();
            SendPacketToAllClientsLoggedIn(data, PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
            StopCoroutine(lac.matchTimer[map, gamemode, num]);
            StopCoroutine(lac.SendMovement[map, gamemode, num]);
        }
        else // lobby still has members, update it
        {
            buffer1.WriteByte(4);
            buffer1.WriteByte(pos);
            Packet packet = new Packet();
            byte[] data = buffer1.ToArray();
            packet.Create(data, data.Length, PacketFlags.Reliable);

            for (int i = 0; i <= slots; i++)
            {
                Debug.Log(i);
                Debug.Log(lac.id[map, gamemode, num, i]);
                peers[lac.id[map, gamemode, num, i]].Send(2, ref packet);
            }


            UpdateLobbySlots(map, gamemode, num, slots);
            
        }
       
       
        buffer1.Dispose();      
    }

    private void UpdateLobbySlots(byte map, byte gamemode, int num, byte slots)
    {
        // notify all clients that a slot was emptied or filled
        ByteBuffer1 buffer2 = new ByteBuffer1();
        buffer2.WriteByte(17);
        buffer2.WriteByte(map);
        buffer2.WriteByte(gamemode);
        buffer2.WriteInteger(num);
        buffer2.WriteByte(slots);
        int id = lac.id[map, gamemode, num, 0];
        buffer2.WriteString(playerName[id]);        
        byte[] data2 = buffer2.ToArray();
        SendPacketToAllClientsLoggedIn(data2, PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
        buffer2.Dispose();
    }



    private void SendPacketToAllClientsLoggedIn(byte[] data, PacketFlags flag, byte channelID)
    {
        Packet packet = new Packet();
        packet.Create(data, data.Length, flag);
        for (int i = 0; i < maxClients; i++)
        {
            if(!String.IsNullOrEmpty(playerName[i]))
            {
                Debug.Log("index: " + i);
                Debug.Log("playername: " + playerName[i]);
                peers[i].Send(channelID, ref packet);
            }
        }     
    }

    void CheckForUnlocks(int id)
    {
        if(xp[id] >= 1150)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteByte(29);
        }
    }

    bool SkinOwned(int peerID, byte skin)
    {
        foreach(byte skinID in skins[peerID])
        {
            if (skinID == skin)
                return true;
        }
        return false;
    }

    bool PowerupOwned(int peerID, byte powerup)
    {
        foreach (byte powerupID in powerups[peerID])
        {
            if (powerupID == powerup)
                return true;
        }
        return false;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < shd.Length; i++)
        {
            if (shd[i] != null)
                shd[i].ClientDisconnected(xp[i], rocks[i], boulderTokens[i], skin[i], powerup[i]);
        }
        ENet.Library.Deinitialize();
        server.Dispose();
    }

}
