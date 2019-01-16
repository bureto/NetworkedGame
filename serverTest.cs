#define NET_4_6

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NetStack.Compression;
using NetStack.Serialization;
using ENet;
using System;
using MySql.Data.MySqlClient;
using System.Globalization;
using Event = ENet.Event;
using EventType = ENet.EventType;
using static Constants;

// NOTE: channel ids for sending packets goes as such:
//  0 for login stuff and initialization, stuff that can wait
//  1 for lobby functions, where latency is kinda important
//  2 for in game functions, important
//  3 for movement since we are sending alot of packets

public class serverTest : MonoBehaviour
{
    Host server;
    Address address;
    ushort port = 40000;
    protected Peer[] peers = new Peer[maxClients];

    private static byte[] byteBuffer = new byte[1024];
    private static IntPtr[] pointerBuffer = new IntPtr[Library.maxPeers];
    private static BitBuffer bitBuffer = new BitBuffer(128);

    private LobbyAndChallenges lac = new LobbyAndChallenges();
    private DataBaseHandler DBH;

    // declared here to reduce GC while handling login/register for each client
    # region loginRegVariables 

    string dbIP;
    string dbPass;
    new string name;
    List<byte> loginSkins = new List<byte>();
    List<byte> loginPowerups = new List<byte>();
    string userORemail;
    string pass;
    string email;
    string user;

    string dateStr;
    #endregion

    string command; // for MySql cmds

    private string[] dateFormats = new string[] { "M/dd/yyyy hh:mm:ss tt", "M/dd/yyyy h:mm:ss tt", "M/d/yyyy hh:mm:ss tt", "M/d/yyyy h:mm:ss tt", "MM/d/yyyy hh:mm:ss tt", "MM/d/yyyy h:mm:ss tt", "MM/dd/yyyy hh:mm:ss tt", "MM/dd/yyyy h:mm:ss tt" };

    private struct LoginRegisterValues
    {
        public int id;
        public bool loggedIn;
        public bool isGuest;
    }

    #region client values
    LoginRegisterValues[] LRV = new LoginRegisterValues[maxClients];
    [NonSerialized]
    public bool[] playerInLobby = new bool[maxClients];
    [NonSerialized]
    public string[] playerName = new string[maxClients];
    private string[] ip = new string[maxClients];
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
    private CompressedVector3[] playerPosition = new CompressedVector3[maxClients];
    private CompressedQuaternion[] playerRotation = new CompressedQuaternion[maxClients];
    #endregion

    void Start()
    {
        ENet.Library.Initialize();
        InitializeLAC();
        DBH = new DataBaseHandler();
        StartServer();
    }
    private void InitializeLAC()
    {
        for (byte m = 0; m < maps; m++)
        {
            for (byte g = 0; g < gamemodes; g++)
            {
                for (int l = 0; l < lobbies; l++)
                {
                    lac.SendMovement[m, g, l] = SendMovement(m, g, l);
                    lac.matchTimer[m, g, l] = MatchTimer(m, g, l);
                    for (int p = 0; p < 4; p++)
                    {
                        lac.id[m, g, l, p] = -1;
                    }
                }
            }
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
        Event netEvent;
        server.Service(0, out netEvent);
        switch (netEvent.Type)
        {
            case EventType.None:
                break;
            case EventType.Connect:
                Connect(netEvent);
                break;

            case EventType.Disconnect:
                Disconnect(netEvent);
                break;

            case EventType.Timeout:
                Disconnect(netEvent);
                break;

            case EventType.Receive:
                ProcessPacket(netEvent);
                break;
        }
    }

    private void Connect(Event netEvent)
    {
        Debug.Log("Client connected (ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ")");
        for (int i = 0; i < maxClients; i++)
        {
            if (!peers[i].IsSet || peers[i].Data == (IntPtr)(-1))
            {
                peers[i] = netEvent.Peer;
                peers[i].Data = new System.IntPtr(i);
                ip[i] = netEvent.Peer.IP;
                LRV[i] = new LoginRegisterValues();
                break;
            }
        }
    }

    private void Disconnect(Event netEvent)
    {
        Debug.Log("Client disconnected (ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ")");
        int peerID = (int)netEvent.Peer.Data;
        peers[peerID].Data = (IntPtr)(-1);
        playerName[peerID] = null;
        // logout player from db
        ClientDisconnected(peerID, xp[peerID], rocks[peerID], boulderTokens[peerID], skin[peerID], powerup[peerID]);
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
            CheckIfGameDone(map, gamemode, num);
        }
    }

    private void ProcessPacket(Event netEvent)
    {
        netEvent.Packet.CopyTo(byteBuffer);
        int peerID = (int)netEvent.Peer.Data;
        // add bytebuffer to bitBuffer & read packet id
        bitBuffer.FromArray(byteBuffer, netEvent.Packet.Length);
        byte packetNum = bitBuffer.ReadByte();

        // handle packet
        switch (packetNum)
        {
            case (byte)ServerPackets.LoadedGame:
                HandleLoadedGame(peerID);
                break;
            case (byte)ServerPackets.QuickPlay:
                HandleQuickPlay(peerID);
                break;
            case (byte)ServerPackets.LoginRegister:
                HandleLogin(peerID);
                break;
            case (byte)ServerPackets.CreateChar:
                HandleCreateChar(peerID);
                break;
            case (byte)ServerPackets.ConnectLobby:
                HandleConnectLobby(peerID);
                break;
            case (byte)ServerPackets.CreateLobby:
                HandleCreateLobby(peerID);
                break;
            case (byte)ServerPackets.CreateChallenge:
                break;
            case (byte)ServerPackets.LeaveLobby:
                HandleLeaveLobby(peerID);
                break;
            case (byte)ServerPackets.Dead:
                HandleDead(peerID);
                break;
            case (byte)ServerPackets.Kick:
                HandleKick(peerID);
                break;
            case (byte)ServerPackets.PlayerPosition:
                HandlePlayerPosition(peerID);
                break;
            case (byte)ServerPackets.HandleExplode:
                HandleExplode(peerID);
                break;
            case (byte)ServerPackets.ObstacleHit:
                HandleObstacleHit(peerID);
                break;
            case (byte)ServerPackets.PlayAsGuest:
                HandlePlayAsGuest(peerID);
                break;
            case (byte)ServerPackets.ReadyUp:
                HandleReadyUp(peerID);
                break;
            case (byte)ServerPackets.EquipItem:
                HandleEquipItem(peerID);
                break;
            case (byte)ServerPackets.PurchaseItem:
                HandlePurchaseItem(peerID);
                break;
            default:
                Debug.Log("Packet id could not be found, packet id:" + packetNum);
                break;
        }

        // clears buffers for re-use
        bitBuffer.Clear();
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        server.Flush();
        netEvent.Packet.Dispose();
    }

    #region Old SHD

    // game stuff
    private void HandlePlayerPosition(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            CompressedVector3 position;
            CompressedQuaternion rotation;

            position.x = (ushort)bitBuffer.ReadUInt();
            position.y = (ushort)bitBuffer.ReadUInt();
            position.z = (ushort)bitBuffer.ReadUInt();

            rotation.m = bitBuffer.ReadByte();
            rotation.a = (short)bitBuffer.ReadInt();
            rotation.b = (short)bitBuffer.ReadInt();
            rotation.c = (short)bitBuffer.ReadInt();
            UpdatePlayerPosition(peerID, position, rotation);
        }
    }

    private void HandleExplode(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            SendExplode(peerID, map, mode, num, pos);
        }
    }

    private void HandleDead(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();
            int distance = bitBuffer.ReadInt();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            Dead(peerID, map, mode, num, pos, distance);
        }
    }

    private void HandlePowerup(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            SendPowerup(peerID, map, mode, num, pos);
        }
    }

    private void HandleObstacleHit(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();

            byte type = bitBuffer.ReadByte();
            string name = bitBuffer.ReadString();
            byte dmg = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            SendObstacleHit(peerID, map, mode, num, pos, type, name, dmg);
        }
    }

    private void HandleReadyUp(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            ReadyUp(peerID, map, mode, num, pos);
        }
    }

    private void HandleLoadedGame(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            OpenGate(peerID);
        }
    }

    // lobby stuff
    private void HandleCreateLobby(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte gamemode = bitBuffer.ReadByte();
            string name = bitBuffer.ReadString();
            string pass = bitBuffer.ReadString();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            CreateLobby(peerID, map, gamemode, name, pass);
        }
    }

    private void HandleCreateChallenge(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            try
            {
                byte map = bitBuffer.ReadByte();
                int seed = bitBuffer.ReadInt();
                string pass = bitBuffer.ReadString();
                string name = playerName[peerID];
                //checks if a seed exists
                using (MySqlConnection sqlConn = DBH.NewConnection())
                {
                    sqlConn.Open();
                    Debug.Log("checking challenges");
                    try
                    {
                        command = @"SELECT seed
                          FROM challenges WHERE seed = @seed";

                        using (MySqlCommand check = new MySqlCommand(command, sqlConn))
                        {
                            check.Parameters.Add("@seed", MySqlDbType.Int32).Value = seed;
                            check.ExecuteScalar().ToString();
                            Debug.Log("seed exists");
                        }

                    }
                    // seed doesn't exist
                    catch
                    {
                        command = "INSERT into challenges (seed, creator)" +
                            "VALUES (@seed, @creator)";
                        using (MySqlCommand create = new MySqlCommand(command, sqlConn))
                        {
                            create.Parameters.Add("@seed", MySqlDbType.Int32).Value = seed;
                            create.Parameters.Add("@creator", MySqlDbType.Int32).Value = name;
                            create.ExecuteScalar().ToString();
                            Debug.Log("created challenge");
                        }
                    }
                }
                //CreateChallenge(peerID, map, seed);
            }
            catch (MySqlException ex)
            {
                //unable to connect
                Debug.Log(ex.Message);
            }
        }
    }

    private void HandleQuickPlay(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte(); // 0 = meadow etc..
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            QuickPlay(peerID, map);
        }
    }

    private void HandleConnectLobby(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            string pass = bitBuffer.ReadString();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            ConnectLobby(peerID, map, mode, num, pass);
        }
    }

    private void HandleLeaveLobby(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            LeaveLobby(peerID, map, mode, num, pos);
        }
    }

    private void HandleKick(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte map = bitBuffer.ReadByte();
            byte mode = bitBuffer.ReadByte();
            int num = bitBuffer.ReadInt();
            byte pos = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            KickPlayer(peerID, map, mode, num, pos);
        }
    }

    // login stuff
    private void HandlePlayAsGuest(int peerID)
    {
        if (!LRV[peerID].loggedIn)
        {
            name = bitBuffer.ReadString(); 
            if (name.Length > 16)
                name = name.Substring(0, 16);
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            LoginSuccess(peerID, name, 1);
            LRV[peerID].loggedIn = true;
            LRV[peerID].isGuest = true;
        }
    }

    private void HandleLogin(int peerID)
    {
        try
        {
            if (!LRV[peerID].loggedIn)
            {
                // open connection to db
                using (MySqlConnection sqlConn = DBH.NewConnection())
                {
                    sqlConn.Open();

                    userORemail = bitBuffer.ReadString();
                    pass = bitBuffer.ReadString();
                    bitBuffer.Clear();
                    Array.Clear(byteBuffer, 0, byteBuffer.Length);

                    if (userORemail.Contains("@") && userORemail.Contains("."))
                    {
                        email = userORemail;
                        user = "";
                        command = @"SELECT id, username, password, email, loginIP, name, xp, skin, rocks, bTokens, timePlayed, soloWins, fourPWins, powerup
                            FROM user WHERE (email = @email)";
                    }
                    else
                    {
                        user = userORemail;
                        email = "";
                        command = @"SELECT id, username, password, email, loginIP, name, xp, skin, rocks, bTokens, timePlayed, soloWins, fourPWins, powerup
                            FROM user WHERE (username = @username)";
                    }
                    int xp = 0;
                    byte skin = 0;
                    int rocks = 0;
                    int boulderTokens = 0;
                    int timePlayed = 0;
                    int soloWins = 0;
                    int fourPlayerWins = 0;
                    byte powerup = 0;
                    loginSkins.Clear();
                    loginSkins.Add(0);
                    loginPowerups.Clear();
                    loginPowerups.Add(0);

                    using (MySqlCommand login = new MySqlCommand(command, sqlConn))
                    {
                        login.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;
                        login.Parameters.Add("@username", MySqlDbType.VarChar).Value = user;
                        using (MySqlDataReader reader = login.ExecuteReader())
                        {
                            if (reader.Read()) //if we are able to find and read the row containing the user/email & pass
                            {
                                // get all stored player values
                                dbIP = reader.GetString("loginIP");
                                dbPass = reader.GetString("password");
                                if (Convert.IsDBNull(reader["name"]))
                                {
                                    name = null;
                                }
                                else
                                {
                                    name = reader.GetString("name");
                                }
                                LRV[peerID].id = reader.GetInt32("id");
                                xp = reader.GetInt32("xp");
                                skin = reader.GetByte("skin");
                                powerup = reader.GetByte("powerup");
                                rocks = reader.GetInt32("rocks");
                                boulderTokens = reader.GetInt32("bTokens");
                                timePlayed = reader.GetInt32("timePlayed");
                                soloWins = reader.GetInt32("soloWins");
                                fourPlayerWins = reader.GetInt32("fourPWins");
                            }
                            else
                            {
                                HandleRegister(peerID);
                                return;
                            }
                        }
                    }
                    if (pass == dbPass)
                    {
                        if (name != null)
                        {
                            if (dbIP == "none") // if not logged in yet
                            {
                                #region get skins and powerups
                                //////////// GET ALL SKINS ////////////////////////////////////////////
                                command = @"SELECT * FROM userSkins WHERE (playerID = @id)";
                                using (MySqlCommand getSkins = new MySqlCommand(command, sqlConn))
                                {
                                    getSkins.Parameters.Add("@id", MySqlDbType.Int32).Value = LRV[peerID].id;
                                    using (MySqlDataReader reader = getSkins.ExecuteReader())
                                    {
                                        if (reader.Read()) //if we are able to find and read the row containing the id
                                        {
                                            int numColumns = reader.FieldCount;
                                            for (byte i = 1; i < numColumns; i++) // i starts at one (skip playerID)
                                            {
                                                if (reader.GetByte(i) == 1) // 1 = owned, 0 = not owned
                                                    loginSkins.Add(i);
                                            }
                                        }
                                        else
                                        {
                                            Debug.Log("id: " + LRV[peerID].id + ", does not exist in userSkins table.");
                                        }
                                    }
                                }

                                //////////// GET ALL POWERUPS ////////////////////////////////////////////
                                command = @"SELECT * FROM userPowerups WHERE (playerID = @id)";
                                using (MySqlCommand getPowerups = new MySqlCommand(command, sqlConn))
                                {
                                    getPowerups.Parameters.Add("@id", MySqlDbType.Int32).Value = LRV[peerID].id;
                                    using (MySqlDataReader reader = getPowerups.ExecuteReader())
                                    {
                                        if (reader.Read()) //if we are able to find and read the row containing the id
                                        {
                                            int numColumns = reader.FieldCount;
                                            for (byte i = 1; i < numColumns; i++) // i starts at one (skip playerID)
                                            {
                                                if (reader.GetByte(i) == 1) // 1 = owned, 0 = not owned
                                                    loginPowerups.Add(i);
                                            }
                                        }
                                        else
                                        {
                                            Debug.Log("id: " + LRV[peerID].id + ", does not exist in powerups table.");
                                        }
                                    }
                                }
                                #endregion

                                // send login to player, and send all stored values
                                LoginSuccess(peerID, name, xp, rocks, boulderTokens, timePlayed, soloWins, fourPlayerWins, loginSkins, loginPowerups, skin, powerup);
                                LRV[peerID].loggedIn = true;

                                //store ip of user connecting, prevents double logins.
                                command = "UPDATE user SET loginIP = @ip WHERE id = @id";
                                using (MySqlCommand setLoginIP = new MySqlCommand(command, sqlConn))
                                {
                                    setLoginIP.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip[peerID];
                                    setLoginIP.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    setLoginIP.ExecuteNonQuery();
                                }
                            }
                            else // logged in already
                            {
                                LoginRegisterResponse(peerID, 5);
                            }
                        }
                        else // still needs to create name (register)
                        {
                            LoginRegisterResponse(peerID, 1);
                        }


                    }
                    else if (dbPass != null)
                    {
                        LoginRegisterResponse(peerID, 2);
                    }
                }
            }
        }
        catch (MySqlException ex)
        {
            //unable to connect
            Debug.Log(ex.Message);
        }
    }

    private void HandleRegister(int peerID)
    {
        // open connection
        using (MySqlConnection sqlConn = DBH.NewConnection())
        {
            //enter the username, password, and email of the new user into the database
            command = "INSERT into user (username, password, email, loginIP)" +
                "VALUES (@username, @password, @email, @ip)";
            using (MySqlCommand register = new MySqlCommand(command, sqlConn))
            {
                register.Parameters.Add("@username", MySqlDbType.VarChar).Value = user;
                register.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;
                register.Parameters.Add("@password", MySqlDbType.VarChar).Value = pass;
                register.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip[peerID];
                register.ExecuteNonQuery();
            }

            // set the skins of new user to their defaults
            command = "INSERT into userSkins () VALUES ()";
            using (MySqlCommand addSkins = new MySqlCommand(command, sqlConn))
            {
                addSkins.ExecuteNonQuery();
            }
            // set the powerups of new user to their defaults
            command = "INSERT into userPowerups () VALUES ()";
            using (MySqlCommand addPowerups = new MySqlCommand(command, sqlConn))
            {
                addPowerups.ExecuteNonQuery();
            }

            //get id of new user
            command = @"SELECT id
                FROM user WHERE (username = @username) AND (email = @email)";
            using (MySqlCommand getID = new MySqlCommand(command, sqlConn))
            {
                getID.Parameters.Add("@username", MySqlDbType.VarChar).Value = user;
                getID.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;
                LRV[peerID].id = (int)getID.ExecuteScalar();
            }
            LoginRegisterResponse(peerID, 1);
        }
    }

    private void HandleCreateChar(int peerID)
    {
        if (!LRV[peerID].loggedIn)
        {
            name = bitBuffer.ReadString();
            if (name.Length > 16)
                name = name.Substring(0, 16);
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            using (MySqlConnection sqlConn = DBH.NewConnection())
            {
                sqlConn.Open();

                command = "UPDATE user SET name = @name WHERE name IS NULL AND id = @id";

                using (MySqlCommand update = new MySqlCommand(command, sqlConn))
                {
                    update.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
                    update.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                    update.ExecuteNonQuery();
                    LoginSuccess(peerID, name, 2);
                    LRV[peerID].loggedIn = true;
                    return;
                }
                Debug.Log("could not name acc");
                return;
            }
            Debug.Log("could not connect to db");
        }
    }

    private void HandlePurchaseItem(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte itemType = bitBuffer.ReadByte();
            byte itemID = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            PurchaseItem(peerID, itemType, itemID);
        }
    }

    public void UnlockSkin(int peerID, int itemID)
    {
        if (LRV[peerID].loggedIn)
        {
            if (!LRV[peerID].isGuest)
            {
                try
                {
                    using (MySqlConnection sqlConn = DBH.NewConnection())
                    {
                        sqlConn.Open();
                        switch (itemID)
                        {
                            case 1:
                                command = "UPDATE userSkins SET 1s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 2:

                                command = "UPDATE userSkins SET 2s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 3:
                                command = "UPDATE userSkins SET 3s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 4:
                                command = "UPDATE userSkins SET 4s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 5:
                                command = "UPDATE userSkins SET 5s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 6:
                                command = "UPDATE userSkins SET 6s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 7:
                                command = "UPDATE userSkins SET 7s = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;
                                
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    //unable to connect
                    Debug.Log(ex.Message);
                }
            }
        }
    }

    public void UnlockPowerup(int peerID, int itemID)
    {
        if (LRV[peerID].loggedIn)
        {
            if (!LRV[peerID].isGuest)
            {
                try
                {
                    using (MySqlConnection sqlConn = DBH.NewConnection())
                    {
                        sqlConn.Open();
                        switch (itemID)
                        {
                            case 1:
                                command = "UPDATE userPowerups SET 1p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 2:
                                command = "UPDATE userPowerups SET 2p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;



                            case 3:
                                command = "UPDATE userPowerups SET 3p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 4:
                                command = "UPDATE userPowerups SET 4p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 5:
                                command = "UPDATE userPowerups SET 5p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 6:
                                command = "UPDATE userPowerups SET 6p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;

                            case 7:
                                command = "UPDATE userPowerups SET 7p = 1 WHERE playerID = @id";
                                using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                                {
                                    addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                                    addPowerup.ExecuteNonQuery();
                                }
                                break;
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    //unable to connect
                    Debug.Log(ex.Message);
                }
            }
        }
    }

    private void HandleEquipItem(int peerID)
    {
        if (LRV[peerID].loggedIn)
        {
            byte itemType = bitBuffer.ReadByte();
            byte itemID = bitBuffer.ReadByte();
            bitBuffer.Clear();
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            EquipItem(peerID, itemType, itemID);
        }
    }

    public void ClientDisconnected(int peerID, int xp, int rocks, int bTokens, byte skin, byte powerup) // called from serverT when client disconnects
    {
        if (LRV[peerID].loggedIn)
        {
            if (!LRV[peerID].isGuest)
            {
                try
                {
                    using (MySqlConnection sqlConn = DBH.NewConnection())
                    {
                        sqlConn.Open();
                        Debug.Log("updateing db logout");
                        command = "UPDATE user SET loginIP = @ip, xp = @xp, rocks = @rocks, bTokens = @bTokens, skin = @skin, powerup = @powerup WHERE id = @id";
                        using (MySqlCommand savePlayerValues = new MySqlCommand(command, sqlConn))
                        {
                            savePlayerValues.Parameters.Add("@id", MySqlDbType.VarChar).Value = LRV[peerID].id;
                            savePlayerValues.Parameters.Add("@ip", MySqlDbType.VarChar).Value = "none";
                            savePlayerValues.Parameters.Add("@xp", MySqlDbType.VarChar).Value = xp;
                            savePlayerValues.Parameters.Add("@rocks", MySqlDbType.VarChar).Value = rocks;
                            savePlayerValues.Parameters.Add("@bTokens", MySqlDbType.VarChar).Value = bTokens;
                            savePlayerValues.Parameters.Add("@skin", MySqlDbType.VarChar).Value = skin;
                            savePlayerValues.Parameters.Add("@powerup", MySqlDbType.VarChar).Value = powerup;
                            savePlayerValues.ExecuteNonQuery();
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    //unable to connect
                    Debug.Log(ex.Message);
                }
            }
        }
    }

    #endregion

    #region Old serverTest
    // game stuff
    IEnumerator StopSendingMovement(byte map, byte mode, int num)
    {
        yield return fiveSeconds;
        StopCoroutine(lac.SendMovement[map, mode, num]);
    }

    IEnumerator MatchTimer(byte map, byte mode, int num)
    {
        while (true)
        {
            yield return oneSecond;
            lac.matchTime[map, mode, num]++;
        }
    }

    IEnumerator SendMovement(byte map, byte mode, int num)
    {
        while (true)
        {
            bitBuffer.AddByte(2);
            bitBuffer.AddByte(lac.slots[map, mode, num]);
            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                bitBuffer.AddUInt(playerPosition[lac.id[map, mode, num, i]].x);
                bitBuffer.AddUInt(playerPosition[lac.id[map, mode, num, i]].y);
                bitBuffer.AddUInt(playerPosition[lac.id[map, mode, num, i]].z);
                
                bitBuffer.AddByte(playerRotation[lac.id[map, mode, num, i]].m);
                bitBuffer.AddInt(playerRotation[lac.id[map, mode, num, i]].a);
                bitBuffer.AddInt(playerRotation[lac.id[map, mode, num, i]].b);
                bitBuffer.AddInt(playerRotation[lac.id[map, mode, num, i]].c);
            }
            bitBuffer.ToArray(byteBuffer);
            Packet packet = new Packet();
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Unsequenced | PacketFlags.UnreliableFragment);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                peers[lac.id[map, mode, num, i]].Send(3, ref packet);
            }
            server.Flush();
            yield return oneTenthSecond;
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
            bitBuffer.AddByte(11);
            bitBuffer.AddByte(slot);
            bitBuffer.ToArray(byteBuffer);
            Packet packet1 = new Packet();
            packet1.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
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
                        return;
                    }
                }

                // open gate on all clients if all players are loaded
                bitBuffer.AddByte(6);
                Packet packet = new Packet();
                bitBuffer.ToArray(byteBuffer);
                packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                Array.Clear(byteBuffer, 0, byteBuffer.Length);
                bitBuffer.Clear();
                for (int i = 0; i <= lac.slots[map, mode, lobby]; i++)
                {
                    peers[lac.id[map, mode, lobby, i]].Send(2, ref packet);
                    lac.loaded[map, mode, lobby, i] = false; // prevents this method from being activated when not supposed to
                }
                StartCoroutine(lac.matchTimer[map, mode, lobby]);
            }
        }
    }

    public void UpdatePlayerPosition(int peerID, CompressedVector3 position, CompressedQuaternion rotation)
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
            bitBuffer.AddByte(20);
            bitBuffer.AddByte(pos);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();

            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                {
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
                }
            }
        }
    }

    public void SendPowerup(int peerID, byte map, byte mode, int num, byte pos)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            bitBuffer.AddByte(31);
            bitBuffer.AddByte(pos);
            bitBuffer.AddByte(powerup[peerID]);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                {
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
                }
            }
        }
    }

    public void SendObstacleHit(int peerID, byte map, byte mode, int num, byte pos, byte type, string name, byte dmg)
    {
        if (lac.id[map, mode, num, pos] == peerID) //verifies client is in lobby, and is in correct 'pos'
        {
            bitBuffer.AddByte(21);
            bitBuffer.AddByte(type);
            bitBuffer.AddString(name);
            bitBuffer.AddByte(dmg);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();

            for (int i = 0; i <= lac.slots[map, mode, num]; i++)
            {
                if (i != pos)
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
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

    public void ReadyUp(int peerID, byte map, byte gamemode, int num, byte pos)
    {
        if (lac.id[map, gamemode, num, pos] == peerID) // verifies pos in lobby
        {
            lac.ready[map, gamemode, num, pos] = true;

            // notify clients in lobby that player readied
            bitBuffer.AddByte(27);
            bitBuffer.AddByte(pos);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
            for (int i = 0; i <= lac.slots[map, gamemode, num]; i++)
            {
                peers[lac.id[map, gamemode, num, i]].Send(1, ref packet);
            }

            // start game if all ready
            if (lac.AllPlayersReady(map, gamemode, num))
            {
                SendStartGame(map, gamemode, num);
            }
        }
    }

    // lobby stuff
    public void QuickPlay(int peerID, byte map)
    {
        if (!playerInLobby[peerID])
        {
            byte gamemode;
            int num;

            if (lac.FindFirstAvailableLobby(peerID, map, out gamemode, out num))
            {
                ConnectLobby(peerID, map, gamemode, num, "");
            }
            else
            {
                bitBuffer.AddByte(23);
                bitBuffer.AddByte(map);
                Packet packet = new Packet();
                bitBuffer.ToArray(byteBuffer);
                packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                peers[peerID].Send(1, ref packet);
                Array.Clear(byteBuffer, 0, byteBuffer.Length);
                bitBuffer.Clear();
            }
        }
    }

    public void ConnectLobby(int peerID, byte map, byte gamemode, int num, string pass)
    {
        if (!playerInLobby[peerID])
        {
            if (!lac.LobbyNull(map, gamemode, num))
            {
                if (!lac.gameStarted[map, gamemode, num])
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
                            bitBuffer.AddByte(10);
                            bitBuffer.AddByte(map);
                            bitBuffer.AddByte(gamemode);
                            bitBuffer.AddInt(num);
                            bitBuffer.AddString(gameName);
                            bitBuffer.AddByte(slots);
                            Packet packet2 = new Packet();
                            bitBuffer.ToArray(byteBuffer);
                            packet2.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                            peers[peerID].Send(1, ref packet2);
                            Array.Clear(byteBuffer, 0, byteBuffer.Length);
                            bitBuffer.Clear();

                            UpdateLobbySlots(map, gamemode, num, slots);


                            // tell all clients that player connected(below)
                            bitBuffer.AddByte(12);
                            bitBuffer.AddString(playerName[peerID]);
                            bitBuffer.AddInt(xp[peerID]);
                            //buffer.WriteByte(skin[peerID]);
                            bitBuffer.AddByte(slots);
                            Packet packet = new Packet();
                            bitBuffer.ToArray(byteBuffer);
                            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                            Array.Clear(byteBuffer, 0, byteBuffer.Length);
                            bitBuffer.Clear();

                            for (byte i = 0; i <= slots; i++)
                            {
                                int loopPeerID = lac.id[map, gamemode, num, i];
                                if (i != slots)
                                {
                                    // tell each client in lobby about new client                       
                                    peers[loopPeerID].Send(1, ref packet);

                                    // tell new client information about each client in lobby
                                    bitBuffer.AddByte(12);
                                    bitBuffer.AddString(playerName[loopPeerID]);
                                    bitBuffer.AddInt(xp[loopPeerID]);
                                    //buffer1.WriteByte(skin[loopPeerID]);
                                    bitBuffer.AddByte(i);
                                    Packet packet1 = new Packet();
                                    bitBuffer.ToArray(byteBuffer);
                                    packet1.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                                    peers[peerID].Send(1, ref packet1);
                                    Array.Clear(byteBuffer, 0, byteBuffer.Length);
                                    bitBuffer.Clear();
                                }

                            }
                            if (slots == (2 * (gamemode + 1)) - 1)
                            {
                                SendStartGame(map, gamemode, num);
                            }
                        }
                        else
                        {
                            /// pass wrong
                            bitBuffer.AddByte(24);
                            Packet packet = new Packet();
                            bitBuffer.ToArray(byteBuffer);
                            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                            peers[peerID].Send(1, ref packet);
                            Array.Clear(byteBuffer, 0, byteBuffer.Length);
                            bitBuffer.Clear();
                        }
                    }
                    else
                    {
                        // lobby full
                        bitBuffer.AddByte(19);
                        bitBuffer.AddByte(2);
                        Packet packet = new Packet();
                        bitBuffer.ToArray(byteBuffer);
                        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                        Array.Clear(byteBuffer, 0, byteBuffer.Length);
                        bitBuffer.Clear();
                    }
                }
                else
                {
                    // game started
                    bitBuffer.AddByte(19);
                    bitBuffer.AddByte(1);
                    Packet packet = new Packet();
                    bitBuffer.ToArray(byteBuffer);
                    packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                    peers[peerID].Send(1, ref packet);
                    Array.Clear(byteBuffer, 0, byteBuffer.Length);
                    bitBuffer.Clear();
                }
            }
            else
            {
                // lobby doesnt exist
                bitBuffer.AddByte(19);
                bitBuffer.AddByte(0);
                Packet packet = new Packet();
                bitBuffer.ToArray(byteBuffer);
                packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                peers[peerID].Send(1, ref packet);
                Array.Clear(byteBuffer, 0, byteBuffer.Length);
                bitBuffer.Clear();
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
            bitBuffer.AddByte(13);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            peers[peerID].Send(1, ref packet);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
            CheckIfGameDone(map, gamemode, num);
        }
    }

    public void KickPlayer(int peerID, byte map, byte gamemode, int num, byte pos)
    {
        if (lac.id[map, gamemode, num, 0] == peerID) // checks to see if it is lobby owner who is sending packet
        {
            int kickedPlayerID = lac.id[map, gamemode, num, pos];
            // subtracts one from slots, opening up a slot for another client
            lac.LeaveLobby(map, gamemode, num, pos);
            playerInLobby[kickedPlayerID] = false;

            // tell other clients that he left
            SendToOthersLeftLobby(map, gamemode, num, pos);

            // notify client that he was kicked from lobby
            bitBuffer.AddByte(18);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            peers[kickedPlayerID].Send(1, ref packet);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
        }

    }

    public void CreateLobby(int peerID, byte map, byte gamemode, string name, string pass)
    {
        if (!playerInLobby[peerID])
        {
            byte Protected = 0;
            // if no name was set, create default name
            if (String.IsNullOrEmpty(name))
            {
                name = playerName[peerID] + lac.gameDefaultNameSuffix;
            }
            // if it is password protected
            if (!String.IsNullOrEmpty(pass))
            {
                Protected = 1;
            }

            int i = lac.CreateLobby(peerID, map, gamemode, playerName[peerID], name, pass); //creates lobby and returns lobby id

            // tell client he successfully created lobby
            playerInLobby[peerID] = true;
            bitBuffer.AddByte(10);
            bitBuffer.AddByte(map);
            bitBuffer.AddByte(gamemode);
            bitBuffer.AddInt(i);
            bitBuffer.AddString(name);
            bitBuffer.AddByte(0);
            //buffer.WriteInteger(gamemode);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
            peers[peerID].Send(1, ref packet);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();

            // update clients with new lobby info
            bitBuffer.AddByte(16);
            bitBuffer.AddByte(map);
            bitBuffer.AddByte(gamemode);
            bitBuffer.AddInt(i);
            bitBuffer.AddString(name);
            bitBuffer.AddString(playerName[peerID]);
            bitBuffer.AddByte(Protected); // tell clients that it is password protected
            bitBuffer.AddByte(0); // tell clients that game not started
            bitBuffer.AddByte(0); // one person in lobby (owner)
            bitBuffer.ToArray(byteBuffer);
            SendPacketToAllClientsLoggedIn(PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
        }
    }

    public void CreateChallenge(int peerID, byte map, int seed)
    {
        bitBuffer.AddByte(14);
        bitBuffer.AddByte(map);
        bitBuffer.AddInt(seed);
        bitBuffer.ToArray(byteBuffer);
        Packet packet = new Packet();
        packet.Create(byteBuffer);
        peers[peerID].Send(1, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
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

                        if (!String.IsNullOrEmpty(gamePass))
                        {
                            Protected = 1;
                        }
                        // update client with new lobby info
                        bitBuffer.AddByte(16);
                        bitBuffer.AddByte(m);
                        bitBuffer.AddByte(g);
                        bitBuffer.AddInt(n);
                        bitBuffer.AddString(gameName);
                        bitBuffer.AddString(playerName[lac.id[m, g, n, 0]]);
                        bitBuffer.AddByte(Protected); // tell clients that it is password protected
                        bitBuffer.AddByte(Convert.ToByte(lac.gameStarted[m, g, n]));
                        bitBuffer.AddByte(slots);
                        Packet packet = new Packet();
                        bitBuffer.ToArray(byteBuffer);
                        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                        Array.Clear(byteBuffer, 0, byteBuffer.Length);
                        bitBuffer.Clear();
                    }
                }
            }
        }
    }

    public void SendToOthersLeftLobby(byte map, byte gamemode, int num, byte pos)
    {
        byte slots = lac.slots[map, gamemode, num];

        // lobby is empty, tell clients to delete it
        if (lac.gameName[map, gamemode, num] == null)
        {
            bitBuffer.AddByte(25);
            bitBuffer.AddByte(map);
            bitBuffer.AddByte(gamemode);
            bitBuffer.AddInt(num);
            bitBuffer.ToArray(byteBuffer);
            SendPacketToAllClientsLoggedIn(PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
            StopCoroutine(lac.matchTimer[map, gamemode, num]);
            StopCoroutine(lac.SendMovement[map, gamemode, num]);
        }
        else // lobby still has members, update it
        {
            bitBuffer.AddByte(4);
            bitBuffer.AddByte(pos);
            Packet packet = new Packet();
            bitBuffer.ToArray(byteBuffer);
            packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable);
            Array.Clear(byteBuffer, 0, byteBuffer.Length);
            bitBuffer.Clear();
            for (int i = 0; i <= slots; i++)
            {
                peers[lac.id[map, gamemode, num, i]].Send(2, ref packet);
            }

            UpdateLobbySlots(map, gamemode, num, slots);
        }

    }

    private void UpdateLobbySlots(byte map, byte gamemode, int num, byte slots)
    {
        // notify all clients that a slot was emptied or filled
        bitBuffer.AddByte(17);
        bitBuffer.AddByte(map);
        bitBuffer.AddByte(gamemode);
        bitBuffer.AddInt(num);
        bitBuffer.AddByte(slots);
        bitBuffer.AddString(playerName[lac.id[map, gamemode, num, 0]]);
        bitBuffer.ToArray(byteBuffer);
        SendPacketToAllClientsLoggedIn(PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
    }

    // login stuff
    public void LoginRegisterResponse(int peerID, byte response) // 1=registered, 2=badlogin, 3=toomanyloginattempts, 4=toomanyconnections, 5=alreadyLoggedIn, 6=tooManyRegistrations, 7=spammingRegistrations
    {
        bitBuffer.AddByte(8);
        bitBuffer.AddByte(response);
        Packet packet = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
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


        bitBuffer.AddByte(7);
        bitBuffer.AddByte(0);
        bitBuffer.AddString(name);
        bitBuffer.AddInt(xp);
        bitBuffer.AddInt(rocks);
        bitBuffer.AddInt(boulderTokens);
        bitBuffer.AddInt(timePlayed);
        bitBuffer.AddInt(soloWins);
        bitBuffer.AddInt(fourPlayerWins);

        bitBuffer.AddByte((byte)skins.Count);
        bitBuffer.AddByte((byte)powerups.Count);
        foreach (byte skinID in skins)
            bitBuffer.AddByte(skinID);
        foreach (byte powerupID in powerups)
            bitBuffer.AddByte(powerupID);
        bitBuffer.AddByte(skin);
        bitBuffer.AddByte(powerup);
        Packet packet = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
    }

    public void LoginSuccess(int peerID, string name, byte guest)    // for newly created accs or for guests
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
        if(this.skins[peerID] == null)
            this.skins[peerID] = new List<byte>();
        else
            this.skins[peerID].Clear();
        this.skins[peerID].Add(0);

        if (this.powerups[peerID] == null)
            this.powerups[peerID] = new List<byte>();
        else
            this.powerups[peerID].Clear();
        this.powerups[peerID].Add(0);
        this.powerup[peerID] = 0;

        bitBuffer.AddByte(7);
        bitBuffer.AddByte(guest); // 2 = newly registered ,since 0 = login, and 1 = guest
        bitBuffer.AddString(name);
        Packet packet = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
    }

    public void AllowLoginRegister(int peerID)
    {
        bitBuffer.AddByte(9);
        Packet packet = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(0, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
    }

    // items
    public void PurchaseItem(int peerID, byte itemType, byte itemID)
    {
        if (itemType == 0)
        {
            if (boulderTokens[peerID] >= skinPrices[itemID])
            {
                if (!SkinOwned(peerID, itemID))
                {
                    skins[peerID].Add(itemID);
                    UnlockSkin(peerID, itemID);
                    boulderTokens[peerID] -= skinPrices[itemID];
                }
                else return;
            }
            else return;
        }
        else if (itemType == 1)
        {
            if (rocks[peerID] >= powerupPrices[itemID])
            {
                if (!PowerupOwned(peerID, itemID))
                {
                    powerups[peerID].Add(itemID);
                    UnlockPowerup(peerID, itemID);
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

        bitBuffer.AddByte(29);
        bitBuffer.AddByte(1);
        bitBuffer.AddByte(itemType);
        bitBuffer.AddByte(itemID);
        Packet packet = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(1, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
    }

    public void EquipItem(int peerID, byte itemType, byte itemID)
    {
        if (itemType == 0)
        {
            if (SkinOwned(peerID, itemID)) // if he does
                skin[peerID] = itemID; // update player to new skin       
            else
                return;
        }
        else if (itemType == 1)
        {
            // Check to see if player has powerup
            if (PowerupOwned(peerID, itemID)) // if he does
                powerup[peerID] = itemID; // simply save the equiped item so when player exits game we can save it in database      
            else
                return;
        }

        // tell client he equiped item
        bitBuffer.AddByte(30);
        bitBuffer.AddByte(itemType);
        bitBuffer.AddByte(itemID);
        Packet packet = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        peers[peerID].Send(1, ref packet);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
    }

    void CheckForUnlocks(int peerID)
    {
        if (xp[peerID] >= 1150)
        {

        }
    }

    bool SkinOwned(int peerID, byte skin)
    {
        foreach (byte skinID in skins[peerID])
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

    // misc
    void SendStartGame(byte map, byte gamemode, int num)
    {
        lac.StartGame(map, gamemode, num);
        StartCoroutine(lac.SendMovement[map, gamemode, num]); // start timer
        // tell clients in lobby to start game
        bitBuffer.AddByte(3);
        bitBuffer.AddInt(lac.gameSeed[map, gamemode, num]);

        bitBuffer.AddByte(lac.slots[map, gamemode, num]);
        for (int i = 0; i <= lac.slots[map, gamemode, num]; i++)
        {
            bitBuffer.AddByte(skin[lac.id[map, gamemode, num, i]]);        // write players skins

            // zero player at start so it wont go to his last game position
            playerPosition[lac.id[map, gamemode, num, i]] = new CompressedVector3(0,0,0);
            playerRotation[lac.id[map, gamemode, num, i]] = SmallestThree.Compress(Quaternion.identity);
        }
        Packet packet4 = new Packet();
        bitBuffer.ToArray(byteBuffer);
        packet4.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();

        for (byte i = 0; i <= lac.slots[map, gamemode, num]; i++)
        {
            int peerID = lac.id[map, gamemode, num, i];
            peers[peerID].Send(1, ref packet4);
        }

        // updates lobby list on clients to tell em game started and cant be joined
        bitBuffer.AddByte(26);
        bitBuffer.AddByte(map);
        bitBuffer.AddByte(gamemode);
        bitBuffer.AddInt(num);
        bitBuffer.AddByte(1);
        bitBuffer.ToArray(byteBuffer);
        SendPacketToAllClientsLoggedIn(PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
        lac.gameStarted[map, gamemode, num] = true;
        Array.Clear(byteBuffer, 0, byteBuffer.Length);
        bitBuffer.Clear();
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
                bitBuffer.AddByte(26);
                bitBuffer.AddByte(map);
                bitBuffer.AddByte(mode);
                bitBuffer.AddInt(num);
                bitBuffer.AddByte(0);
                bitBuffer.ToArray(byteBuffer);
                SendPacketToAllClientsLoggedIn(PacketFlags.Reliable | PacketFlags.Unsequenced, 1);
                Array.Clear(byteBuffer, 0, byteBuffer.Length);
                bitBuffer.Clear();

                // tells clients in lobby game is done
                bitBuffer.AddByte(28);
                if (lac.slots[map, mode, num] < (((mode + 1) * 2) - 1))
                    bitBuffer.AddByte(0); // not enough players
                else
                    bitBuffer.AddByte(1); // enough players
                bitBuffer.AddInt(lac.matchTime[map, mode, num]);
                bitBuffer.AddByte((byte)firstPlacePlayerPos);
                bitBuffer.ToArray(byteBuffer);
                Packet packet = new Packet();
                packet.Create(byteBuffer, bitBuffer.Length, PacketFlags.Reliable | PacketFlags.Unsequenced);
                Array.Clear(byteBuffer, 0, byteBuffer.Length);
                bitBuffer.Clear();
                for (int i = 0; i <= lac.slots[map, mode, num]; i++)
                {
                    peers[lac.id[map, mode, num, i]].Send(2, ref packet);
                }

            }
        }
    }

    private void SendPacketToAllClientsLoggedIn(PacketFlags flag, byte channelID)
    {
        Packet packet = new Packet();
        packet.Create(byteBuffer, bitBuffer.Length, flag);
        for (int i = 0; i < maxClients; i++)
        {
            if (!String.IsNullOrEmpty(playerName[i]))
            {
                Debug.Log("index: " + i);
                Debug.Log("playername: " + playerName[i]);
                peers[i].Send(channelID, ref packet);
            }
        }
    }

    #endregion

    private void OnDestroy()
    {
        for (int i = 0; i < maxClients; i++)
        {
            if (peers[i].Data != null)
                ClientDisconnected(i, xp[i], rocks[i], boulderTokens[i], skin[i], powerup[i]);
        }
        ENet.Library.Deinitialize();
        server.Dispose();
    }
}