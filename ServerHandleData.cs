using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Timers;
using System.Globalization;
using System;
using MySql.Data.MySqlClient;

public class ServerHandleData
{
    private delegate void Packet_(byte[] data, int peerID);
    private Dictionary<byte, Packet_> Packets;
    private serverTest serverT;

    string command;


    private string ip;
    private int id = 0;
    private bool loggedIn;
    private bool isGuest;


    #region login and reg vars
    private int loginAttempt = 0;
    private int newAccs = 0;
    private int waitTime = 120000; //for login cooldown
    private int waitTime1 = 120000; //for registration cooldown
    private bool regCD = false;
    private bool loginCD = false;
    private DateTime currentDate;
    private bool regCapCleared = true;

    private Timer timer;
    private Timer timer1;
    private bool timerOn = false;
    private bool timer1On = false;
    #endregion
    private string[] dateFormats = new string[] { "M/dd/yyyy hh:mm:ss tt", "M/dd/yyyy h:mm:ss tt", "M/d/yyyy hh:mm:ss tt", "M/d/yyyy h:mm:ss tt", "MM/d/yyyy hh:mm:ss tt", "MM/d/yyyy h:mm:ss tt", "MM/dd/yyyy hh:mm:ss tt", "MM/dd/yyyy h:mm:ss tt" };

    // Use this for initialization
    public void Initialize(serverTest st, string ip)
    {
        serverT = st;
        this.ip = ip;
     
        InitializeMessages();

    }
    void InitializeMessages()
    {
        Packets = new Dictionary<byte, Packet_>();
        Packets.Add((byte)ServerPackets.LoadedGame, HandleLoadedGame);
        Packets.Add((byte)ServerPackets.Movement, HandleMovement);
        Packets.Add((byte)ServerPackets.QuickPlay, HandleQuickPlay);
        Packets.Add((byte)ServerPackets.LoginRegister, HandleLogin);
        Packets.Add((byte)ServerPackets.CreateChar, HandleCreateChar);
        Packets.Add((byte)ServerPackets.ConnectLobby, HandleConnectLobby);
        Packets.Add((byte)ServerPackets.CreateLobby, HandleCreateLobby);
        Packets.Add((byte)ServerPackets.LeaveLobby, HandleLeaveLobby);
        Packets.Add((byte)ServerPackets.CreateChallenge, HandleCreateChallenge);
        Packets.Add((byte)ServerPackets.PlayerPosition, HandlePlayerPosition);
        Packets.Add((byte)ServerPackets.HandleExplode, HandleExplode);
        Packets.Add((byte)ServerPackets.ObstacleHit, HandleObstacleHit);
        Packets.Add((byte)ServerPackets.PlayAsGuest, HandlePlayAsGuest);
        Packets.Add((byte)ServerPackets.Kick, HandleKick);
        Packets.Add((byte)ServerPackets.Dead, HandleDead);
        Packets.Add((byte)ServerPackets.ReadyUp, HandleReadyUp);
        Packets.Add((byte)ServerPackets.EquipItem, HandleEquipItem);
        Packets.Add((byte)ServerPackets.PurchaseItem, HandlePurchaseItem);
        Packets.Add((byte)ServerPackets.Powerup, HandlePowerup);

    }

    public void HandleNetworkMessages(byte[] data, int peerID)
    {
        // read packet id and remove it from byte array
        byte packetNum = data[0];
        byte[] packet = new byte[data.Length - 1];
        Buffer.BlockCopy(data, 1, packet, 0, packet.Length);
        Packet_ Packet;
        //Debug.Log(packetNum);
        if (Packets.ContainsKey(packetNum))
        {
            Packet = Packets[packetNum];
            Packet.Invoke(packet, peerID);
        }
    }

    private void HandlePlayerPosition(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            // max values for velocity = (6 or -6, 4.7, 6 or -6)
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            Vector3 position = buffer.ReadVector3();
            Vector3 rotation = buffer.ReadVector3();

            serverT.UpdatePlayerPosition(peerID, position, rotation);
        }
    }

    private void HandleExplode(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();

            serverT.SendExplode(peerID, map, mode, num, pos);
            buffer.Dispose();
        }
    }

    private void HandleDead(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();
            int distance = buffer.ReadInteger();

            serverT.Dead(peerID, map, mode, num, pos, distance);
            buffer.Dispose();
        }
    }

    private void HandlePowerup(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();
            serverT.SendPowerup(peerID, map, mode, num, pos);
            buffer.Dispose();
        }
    }


    private void HandleObstacleHit(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();

            byte type = buffer.ReadByte();
            string name = buffer.ReadString();
            float dmg = buffer.ReadFloat();

            serverT.SendObstacleHit(peerID, map, mode, num, pos, type, name, dmg);
            buffer.Dispose();
        }
    }


    private void HandleMovement(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            // max values for velocity = (6 or -6, 4.7, 6 or -6)
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();
            //int sequence = buffer.ReadInteger();
            byte input = buffer.ReadByte();
            buffer.Dispose();

            #region prevent speed hack
            if (input > 1 || input < 0)
            {
                input = 0;
            }
            #endregion

            serverT.MovePlayers(peerID, map, mode, num, pos, input);
        }
    }

    private void HandlePlayAsGuest(byte[] data, int peerID)
    {
        if (!loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            string name = buffer.ReadString(); // 0 = meadow etc..
            if (name.Length > 16)
                name = name.Substring(0, 16);
            serverT.LoginSuccess(peerID, name, 1);
            buffer.Dispose();
            loggedIn = true;
            isGuest = true;
        }
    }

    private void HandleQuickPlay(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            byte map = buffer.ReadByte(); // 0 = meadow etc..
            serverT.QuickPlay(peerID, map);
            buffer.Dispose();
        }
    }

    private void HandleConnectLobby(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            string pass = buffer.ReadString();
            serverT.ConnectLobby(peerID, map, mode, num, pass);
            buffer.Dispose();
        }
    }


    private void HandleReadyUp(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();
            serverT.ReadyUp(peerID, map, mode, num, pos);
            buffer.Dispose();
        }
    }

    private void HandleLeaveLobby(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();
            serverT.LeaveLobby(peerID, map, mode, num, pos);
            buffer.Dispose();
        }
    }

    private void HandleKick(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            Debug.Log("handle kick");
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            byte map = buffer.ReadByte();
            byte mode = buffer.ReadByte();
            int num = buffer.ReadInteger();
            byte pos = buffer.ReadByte();
            serverT.KickPlayer(peerID, map, mode, num, pos);
            buffer.Dispose();
        }
    }



    private void HandleCreateLobby(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            byte map = buffer.ReadByte();
            byte gamemode = buffer.ReadByte();
            string name = buffer.ReadString();
            string pass = buffer.ReadString();
            buffer.Dispose();
            serverT.CreateLobby(peerID, map, gamemode, name, pass);
        }
    }

    private void HandleCreateChallenge(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            try
            {
                ByteBuffer1 buffer = new ByteBuffer1();
                buffer.WriteBytes(data);

                byte map = buffer.ReadByte();
                int seed = buffer.ReadInteger();
                string pass = buffer.ReadString();
                string name = serverT.playerName[peerID];
                buffer.Dispose();

                //checks if a seed exists
                using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
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
                //serverT.CreateChallenge(peerID, map, seed);
            }
            catch (MySqlException ex)
            {
                //unable to connect
                Debug.Log(ex.Message);
            }
        }
    }


    private void HandleLoadedGame(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            serverT.OpenGate(peerID);
        }
    }

    private void HandleLogin(byte[] data, int peerID)
    {
        if (!loggedIn)
        {
            try
            {
                ByteBuffer1 buffer;
                buffer = new ByteBuffer1();
                buffer.WriteBytes(data); //send login sent data in format, int(packet#), int(userlength), string(username), int(passlength), string(passlength)

                string userORemail = buffer.ReadString(); //in ReadString(), it calls ReadInt() so we do not have to call it here to get the length of string
                string pass = buffer.ReadString();
                buffer.Dispose();
                string email;
                string user;
                using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
                {
                    sqlConn.Open();
                    // check for spam / invalid login attempts
                    try
                    {
                        command = @"SELECT loginAttempts
                            FROM login WHERE ip = @ip";
                        using (MySqlCommand getLoginAttempts = new MySqlCommand(command, sqlConn))
                        {
                            getLoginAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;

                            loginAttempt = (int)getLoginAttempts.ExecuteScalar();
                        }
                    }
                    catch
                    {
                        Debug.Log("ip is not in login table");
                        command = "INSERT INTO login(ip) VALUES(@ip)";
                        using (MySqlCommand setLoginAttempts = new MySqlCommand(command, sqlConn))
                        {
                            setLoginAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                            loginAttempt = 0;
                            setLoginAttempts.ExecuteNonQuery();
                        }
                    }
                    
                    if (loginAttempt >= 10) //if over 10 invalid login attempts
                    {
                        loginCD = true;

                        try
                        {
                            //check if there is an existing restriction
                            command = @"SELECT dateOfLoginCD
                                FROM login WHERE ip = @ip";
                            string dateStr;
                            using (MySqlCommand checkDate = new MySqlCommand(command, sqlConn))
                            {
                                checkDate.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                dateStr = checkDate.ExecuteScalar().ToString();
                            }

                            // if a date exists, (there is a restriction)
                            if (dateStr != "none")
                            {
                                //check if waittime has passed    
                                if (DateTime.ParseExact(dateStr, dateFormats,
                                               CultureInfo.InvariantCulture, DateTimeStyles.None) < DateTime.Now)
                                {
                                    command = "UPDATE login SET dateOfLoginCD = @date WHERE ip = @ip";
                                    using (MySqlCommand removeRestriction = new MySqlCommand(command, sqlConn))
                                    {
                                        removeRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                        removeRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = "none";
                                        removeRestriction.ExecuteNonQuery();
                                    }

                                    command = "UPDATE login SET loginAttempts = " + "0" + " WHERE ip = @ip";
                                    using (MySqlCommand removeAttempts = new MySqlCommand(command, sqlConn))
                                    {
                                        removeAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                        removeAttempts.ExecuteNonQuery();
                                    }
                                    loginCD = false;
                                    loginAttempt = 0;
                                }
                                else
                                {
                                    if (timerOn == false)
                                    {
                                        DateTime startTime = DateTime.Now;
                                        DateTime endTime = DateTime.ParseExact(dateStr, dateFormats,
                                               CultureInfo.InvariantCulture, DateTimeStyles.None);
                                        TimeSpan span = endTime.Subtract(startTime);
                                        float decmin = (float)span.Seconds / 60;
                                        decmin += span.Minutes;
                                        decmin += span.Milliseconds / 60000;
                                        serverT.LoginRegisterResponse(peerID, 3);
                                        int time = (byte)(decmin * 60000);
                                        timer = new Timer();
                                        timer1.Elapsed += (source, e) => OnTimedEvent(peerID, source, e);
                                        timer.Interval = time;
                                        timer.AutoReset = false;
                                        timer.Enabled = true;
                                        timerOn = true;
                                    }
                                }
                            }
                            else
                            {
                                if (timerOn == false)
                                {
                                    serverT.LoginRegisterResponse(peerID, 3);
                                    currentDate = DateTime.Now;
                                    command = "UPDATE login SET dateOfLoginCD = @date WHERE ip = @ip";
                                    using (MySqlCommand addRestriction = new MySqlCommand(command, sqlConn))
                                    {
                                        addRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                        addRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = currentDate.AddMinutes(waitTime / 60000);
                                        addRestriction.ExecuteNonQuery();
                                    }
                                    timer = new Timer();
                                    timer.Elapsed += (source, e) => OnTimedEvent(peerID, source, e);
                                    timer.Interval = waitTime;
                                    timer.AutoReset = false;
                                    timer.Enabled = true;
                                    timerOn = true;
                                }
                            }
                        }

                        catch (MySqlException ex)
                        {
                            // error while doing something
                            Debug.Log(ex.Message);

                        }
                    }

                    if (loginCD == false)
                    {
                        int connected = 0;
                        //dont allow more than 12 serverT.DBH.connections per ip
                        command = @"SELECT COUNT(loginIP)
                          FROM user WHERE (loginIP = @ip) GROUP BY loginIP HAVING (COUNT(loginIP) > 1)";
                        using (MySqlCommand countConnected = new MySqlCommand(command, sqlConn))
                        {
                            countConnected.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                            try
                            {
                                connected = int.Parse(countConnected.ExecuteScalar().ToString());
                            }
                            catch
                            {
                                connected = 0; //nobody connected on this ip
                            }

                        }
                        if (connected >= 12) //if we are able to find more then 12 serverT.DBH.connections
                        {
                            serverT.LoginRegisterResponse(peerID, 4);
                        }

                        else
                        {
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
                            string dbIP = null;
                            string dbPass = null;
                            string name = null;
                            int xp = 0;
                            byte skin = 0;
                            int rocks = 0;
                            int boulderTokens = 0;
                            int timePlayed = 0;
                            int soloWins = 0;
                            int fourPlayerWins = 0;
                            byte powerup = 0;
                            List<byte> skins = new List<byte>(new byte[1]);
                            List<byte> powerups = new List<byte>(new byte[1]);
                            using (MySqlCommand login = new MySqlCommand(command, sqlConn))
                            {
                                login.Parameters.Add("@email", MySqlDbType.VarChar).Value = email; //sets parameter to email sent from login request, allows for dynamically trying different emails/usernames in the same function
                                login.Parameters.Add("@username", MySqlDbType.VarChar).Value = user;
                                using (MySqlDataReader reader = login.ExecuteReader())
                                {
                                    if (reader.Read()) //if we are able to find and read the row containing the mail/pass
                                    {
                                        // get all stored player values
                                        dbIP = reader["loginIP"].ToString();
                                        dbPass = reader["password"].ToString();
                                        if (Convert.IsDBNull(reader["name"]))
                                        {
                                            name = null;
                                        }
                                        else
                                        {
                                            name = reader["name"].ToString();
                                        }
                                        id = (int)reader["id"];
                                        xp = (int)reader["xp"];
                                        skin = reader.GetByte("skin");
                                        powerup = reader.GetByte("powerup");
                                        rocks = (int)reader["rocks"];
                                        boulderTokens = (int)reader["bTokens"];
                                        timePlayed = (int)reader["timePlayed"];
                                        soloWins = (int)reader["soloWins"];
                                        fourPlayerWins = (int)reader["fourPWins"];
                                    }
                                    else
                                    {
                                        HandleRegister(peerID, user, email, pass);
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
                                            getSkins.Parameters.Add("@id", MySqlDbType.Int32).Value = id;
                                            using (MySqlDataReader reader = getSkins.ExecuteReader())
                                            {
                                                if (reader.Read()) //if we are able to find and read the row containing the id
                                                {
                                                    int numColumns = reader.FieldCount;
                                                    for (byte i = 1; i < numColumns; i++) // i starts at one (skip playerID)
                                                    {
                                                        if (reader.GetByte(i) == 1) // 1 = owned, 0 = not owned
                                                            skins.Add(i);
                                                    }
                                                }
                                                else
                                                {
                                                    Debug.Log("id: " + id + ", does not exist in userSkins table.");
                                                }
                                            }
                                        }
                                        ///////// END OF GETTING SKINS///////////////////

                                        //////////// GET ALL POWERUPS ////////////////////////////////////////////
                                        command = @"SELECT * FROM userPowerups WHERE (playerID = @id)";
                                        using (MySqlCommand getPowerups = new MySqlCommand(command, sqlConn))
                                        {
                                            getPowerups.Parameters.Add("@id", MySqlDbType.Int32).Value = id;
                                            using (MySqlDataReader reader = getPowerups.ExecuteReader())
                                            {
                                                if (reader.Read()) //if we are able to find and read the row containing the id
                                                {
                                                    int numColumns = reader.FieldCount;
                                                    for (byte i = 1; i < numColumns; i++) // i starts at one (skip playerID)
                                                    {
                                                        if (reader.GetByte(i) == 1) // 1 = owned, 0 = not owned
                                                            powerups.Add(i);
                                                    }
                                                }
                                                else
                                                {
                                                    Debug.Log("id: " + id + ", does not exist in powerups table.");
                                                }
                                            }
                                        }
                                        ///////// END OF GETTING POWERUPS///////////////////
                                        #endregion

                                        Debug.Log("Player " + id + "  logged in, user: " + user + ", email: " + email + ", name: " + name);

                                        // send login to player, and send all stored values
                                        serverT.LoginSuccess(peerID, name, xp, rocks, boulderTokens, timePlayed, soloWins, fourPlayerWins, skins, powerups, skin, powerup);
                                        loggedIn = true;

                                        //store ip of user connecting, prevents double logins.
                                        command = "UPDATE user SET loginIP = @ip WHERE id = @id";
                                        using (MySqlCommand setLoginIP = new MySqlCommand(command, sqlConn))
                                        {
                                            setLoginIP.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                            setLoginIP.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                            setLoginIP.ExecuteNonQuery();
                                        }
                                    }
                                    else // logged in already
                                    {
                                        serverT.LoginRegisterResponse(peerID, 5);
                                    }
                                }
                                else // still needs to create name (register)
                                {
                                    serverT.LoginRegisterResponse(peerID, 1);
                                }

                            }
                            else if (dbPass != null) //if password exists in db and user got it wrong
                            {
                                Debug.Log("Player unsuccessful log in, IP: " + ip);
                                serverT.LoginRegisterResponse(peerID, 2);
                                command = "UPDATE login SET loginAttempts = @loginAttempts WHERE ip = @ip";
                                using (MySqlCommand addLoginAttempt = new MySqlCommand(command, sqlConn))
                                {
                                    addLoginAttempt.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                    addLoginAttempt.Parameters.Add("@loginAttempts", MySqlDbType.VarChar).Value = (loginAttempt + 1);
                                    addLoginAttempt.ExecuteNonQuery();
                                }
                            }
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
    }

    private void HandleRegister(int peerID, string user, string email, string pass)
    {
        Debug.Log("retgistering");
        try
        {
            using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
            {
                sqlConn.Open();
                command = @"SELECT newAccounts
                          FROM login WHERE ip = @ip";
                using (MySqlCommand getAccs = new MySqlCommand(command, sqlConn))
                {
                    getAccs.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                    newAccs = (int)getAccs.ExecuteScalar();
                }
                // add one to new accs (a.k.a. registration attempts) since this will only be called if an account with user/email doesnt exist 
                command = "UPDATE login SET newAccounts = @newAccounts WHERE ip = @ip";
                using (MySqlCommand addAccs = new MySqlCommand(command, sqlConn))
                {

                    addAccs.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                    addAccs.Parameters.Add("@newAccounts", MySqlDbType.VarChar).Value = newAccs += 1;
                    addAccs.ExecuteNonQuery();
                }

                //check if was spamming registrations
                if (newAccs >= 22)
                {
                    regCD = true;
                    regCapCleared = false;
                    try
                    {
                        //check if there is an existing restriction
                        command = @"SELECT dateOfRegCD
                          FROM login WHERE ip = @ip";
                        string dateStr;
                        using (MySqlCommand check = new MySqlCommand(command, sqlConn))
                        {
                            check.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                            dateStr = check.ExecuteScalar().ToString();
                        }
                        if (dateStr != "none")
                        {
                            //check if waittime has passed
                            if (DateTime.ParseExact(dateStr, dateFormats,
                                            CultureInfo.InvariantCulture, DateTimeStyles.None) < DateTime.Now)
                            {
                                command = "UPDATE login SET dateOfRegCD = @date WHERE ip = @ip";
                                using (MySqlCommand removeRestriction = new MySqlCommand(command, sqlConn))
                                {
                                    removeRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                    removeRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = "none";
                                    removeRestriction.ExecuteNonQuery();
                                }

                                command = "UPDATE login SET newAccounts = " + "12" + " WHERE ip = @ip";
                                using (MySqlCommand setAttempts = new MySqlCommand(command, sqlConn))
                                {
                                    setAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                    setAttempts.ExecuteNonQuery();
                                }
                                regCD = false;
                                newAccs = 12;
                            }
                            else
                            {
                                if (timer1On == false)
                                {
                                    DateTime startTime = DateTime.Now;
                                    DateTime endTime = DateTime.ParseExact(dateStr, dateFormats,
                                            CultureInfo.InvariantCulture, DateTimeStyles.None);
                                    TimeSpan span = endTime.Subtract(startTime);
                                    float decmin = (float)span.Seconds / 60;
                                    decmin += span.Minutes;
                                    decmin += span.Milliseconds / 60000;
                                    int time = (byte)(decmin * 60000);
                                    serverT.LoginRegisterResponse(peerID, 7);
                                    timer1 = new Timer();
                                    timer1.Elapsed += (source, e) => OnTimedEvent1(peerID, source, e);
                                    timer1.Interval = time;
                                    timer1.AutoReset = false;
                                    timer1.Enabled = true;
                                    timer1On = true;
                                }
                            }
                        }
                        else
                        {
                            if (timer1On == false)
                            {
                                serverT.LoginRegisterResponse(peerID, 7);
                                currentDate = DateTime.Now;
                                command = "UPDATE login SET dateOfRegCD = @date WHERE ip = @ip";
                                using (MySqlCommand addRestriction = new MySqlCommand(command, sqlConn))
                                {
                                    addRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                    addRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = currentDate.AddMinutes(waitTime1 / 60000);
                                    addRestriction.ExecuteNonQuery();
                                }
                                timer1 = new Timer();
                                timer1.Elapsed += (source, e) => OnTimedEvent1(peerID, source, e);
                                timer1.Interval = waitTime1;
                                timer.AutoReset = false;
                                timer1.Enabled = true;
                                timer1On = true;
                            }
                        }
                    }

                    catch (MySqlException ex)
                    {
                        // error doing something
                        Debug.Log(ex.Message);

                    }
                }
                if (newAccs >= 12) //if over 12 registrations
                {
                    regCapCleared = false;
                    try
                    {
                        //check if there is an existing restriction
                        command = @"SELECT dateOfRegistrationCap
                          FROM login WHERE ip = @ip";
                        string dateStr;
                        using (MySqlCommand getRestriction = new MySqlCommand(command, sqlConn))
                        {
                            getRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                            dateStr = getRestriction.ExecuteScalar().ToString();
                        }
                        if (dateStr != "none")
                        {
                            //check if 24 hrs has passed
                            if (DateTime.ParseExact(dateStr, dateFormats,
                                            CultureInfo.InvariantCulture, DateTimeStyles.None) < DateTime.Now)
                            {
                                command = "UPDATE login SET dateOfRegistrationCap = @date WHERE ip = @ip";
                                using (MySqlCommand removeRestriction = new MySqlCommand(command, sqlConn))
                                {
                                    removeRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                    removeRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = "none";
                                    removeRestriction.ExecuteNonQuery();
                                }

                                command = "UPDATE login SET newAccounts = " + "0" + " WHERE ip = @ip";
                                using (MySqlCommand removeAttempts = new MySqlCommand(command, sqlConn))
                                {
                                    removeAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                    removeAttempts.ExecuteNonQuery();
                                }
                                regCapCleared = true;
                                newAccs = 0;

                            }
                            else
                            {
                                if (regCD == false)
                                    serverT.LoginRegisterResponse(peerID, 6);
                            }
                        }
                        else
                        {
                            currentDate = DateTime.Now;
                            command = "UPDATE login SET dateOfRegistrationCap = @date WHERE ip = @ip";
                            using (MySqlCommand addRestriction = new MySqlCommand(command, sqlConn))
                            {
                                addRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                                addRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = currentDate.AddDays(1);
                                addRestriction.ExecuteNonQuery();
                            }
                            serverT.LoginRegisterResponse(peerID, 6);
                        }
                    }
                    catch (MySqlException ex)
                    {
                        // error doing something
                        Debug.Log(ex.Message);

                    }
                }

                //if eligible to register
                else if (regCapCleared == true)
                {
                    //enter the username, password, and email of the new user into the database
                    command = "INSERT into user (username, password, email, loginIP)" +
                        "VALUES (@username, @password, @email, @ip)";
                    using (MySqlCommand register = new MySqlCommand(command, sqlConn))
                    {
                        register.Parameters.Add("@username", MySqlDbType.VarChar).Value = user;
                        register.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;
                        register.Parameters.Add("@password", MySqlDbType.VarChar).Value = pass;
                        register.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
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
                        id = (int)getID.ExecuteScalar();
                    }
                    serverT.LoginRegisterResponse(peerID, 1);
                }
            }
        }
        catch (MySqlException ex)
        {
            //unable to connect
            Debug.Log(ex.Message);
        }
    }

    private void HandleCreateChar(byte[] data, int peerID)
    {
        if (!loggedIn)
        {

            string name;
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);

            name = buffer.ReadString();
            if (name.Length > 16)
                name = name.Substring(0, 16);

            using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
            {
                sqlConn.Open();

                Debug.Log(id);
                command = "UPDATE user SET name = @name WHERE name IS NULL AND id = @id";

                using (MySqlCommand update = new MySqlCommand(command, sqlConn))
                {
                    update.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
                    update.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                    update.ExecuteNonQuery();
                    Debug.Log("named acc");
                    serverT.LoginSuccess(peerID, name, 2);
                    loggedIn = true;
                    return;
                }
                Debug.Log("could not name acc");
                return;
            }
            Debug.Log("could not connect to db");
        }
    }
    

    #region login and reg timers
    public void OnTimedEvent(int peerID, object source, ElapsedEventArgs e)
    {
        if (timerOn)
        {
            try
            {
                //reset the number of incorrect login attempts
                using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
                {
                    sqlConn.Open();
                    command = "UPDATE login SET loginAttempts = " + "0" + " WHERE ip = @ip";
                    using (MySqlCommand removeAttempts = new MySqlCommand(command, sqlConn))
                    {
                        removeAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                        removeAttempts.ExecuteNonQuery();
                    }
                    loginAttempt = 0;
                    loginCD = false;

                    //remove date of cooldown
                    command = "UPDATE login SET dateOfLoginCD = @date WHERE ip = @ip";
                    using (MySqlCommand removeRestriction = new MySqlCommand(command, sqlConn))
                    {
                        removeRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                        removeRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = "none";
                        removeRestriction.ExecuteNonQuery();
                    }
                    serverT.AllowLoginRegister(peerID);
                    timer.Stop();
                    timerOn = false;
                    
                }
            }
            catch (MySqlException ex)
            {
                //unable to connect
                Debug.Log(ex.Message);
            }
        }
    }


    public void OnTimedEvent1(int peerID, object source, ElapsedEventArgs e)
    {
        if (timer1On)
        {
            try
            {
                using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
                {
                    sqlConn.Open();
                    //refresh newAccount amount
                    command = "UPDATE login SET newAccounts = " + "12" + " WHERE ip = @ip";
                    using (MySqlCommand setAttempts = new MySqlCommand(command, sqlConn))
                    {
                        setAttempts.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                        setAttempts.ExecuteNonQuery();
                    }
                    newAccs = 12;
                    regCD = false;


                    //remove date of cooldown
                    command = "UPDATE login SET dateOfRegCD = @date WHERE ip = @ip";
                    using (MySqlCommand removeRestriction = new MySqlCommand(command, sqlConn))
                    {
                        removeRestriction.Parameters.Add("@ip", MySqlDbType.VarChar).Value = ip;
                        removeRestriction.Parameters.Add("@date", MySqlDbType.VarChar).Value = "none";
                        removeRestriction.ExecuteNonQuery();
                    }
                    timer1On = false;
                    serverT.AllowLoginRegister(peerID);
                    
                }
            }
            catch (MySqlException ex)
            {
                //unable to connect
                Debug.Log(ex.Message);
            }
        }
    }
    #endregion

    private void HandlePurchaseItem(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte itemType = buffer.ReadByte();
            byte itemID = buffer.ReadByte();
            serverT.PurchaseItem(peerID, itemType, itemID);
        }
    }

    public void UnlockSkin(int itemID)
    {
        if (loggedIn)
        {
            if (!isGuest)
            {
                try
                {
                    using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
                    {
                        sqlConn.Open();
                        if (itemID == 1)
                        {
                            command = "UPDATE userSkins SET 1s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 2)
                        {
                            command = "UPDATE userSkins SET 2s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 3)
                        {
                            command = "UPDATE userSkins SET 3s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 4)
                        {
                            command = "UPDATE userSkins SET 4s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 5)
                        {
                            command = "UPDATE userSkins SET 5s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 6)
                        {
                            command = "UPDATE userSkins SET 6s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 7)
                        {
                            command = "UPDATE userSkins SET 7s = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
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

    public void UnlockPowerup(int itemID)
    {
        Debug.Log("item id:" + itemID);
        if (loggedIn)
        {
            if (!isGuest)
            {
                try
                {
                    using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
                    {
                        Debug.Log("updated ta");
                        sqlConn.Open();
                        if (itemID == 1)
                        {
                            Debug.Log("updated ta");
                            command = "UPDATE userPowerups SET 1p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                                Debug.Log("updated table");
                            }
                            return;
                        }
                        if (itemID == 2)
                        {
                            command = "UPDATE userPowerups SET 2p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 3)
                        {
                            command = "UPDATE userPowerups SET 3p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 4)
                        {
                            command = "UPDATE userPowerups SET 4p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 5)
                        {
                            command = "UPDATE userPowerups SET 5p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 6)
                        {
                            command = "UPDATE userPowerups SET 6p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
                        }
                        if (itemID == 7)
                        {
                            command = "UPDATE userPowerups SET 7p = 1 WHERE playerID = @id";
                            using (MySqlCommand addPowerup = new MySqlCommand(command, sqlConn))
                            {
                                addPowerup.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
                                addPowerup.ExecuteNonQuery();
                            }
                            return;
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


    private void HandleEquipItem(byte[] data, int peerID)
    {
        if (loggedIn)
        {
            ByteBuffer1 buffer = new ByteBuffer1();
            buffer.WriteBytes(data);
            byte itemType = buffer.ReadByte();
            byte itemID = buffer.ReadByte();
            serverT.EquipItem(peerID, itemType, itemID);
        }
    }


    public void ClientDisconnected(int xp, int rocks, int bTokens, byte skin, byte powerup) // called from serverT when client disconnects
    {
        if (loggedIn)
        {
            if (!isGuest)
            {
                try
                {
                    using (MySqlConnection sqlConn = serverT.DBH.NewConnection())
                    {
                        sqlConn.Open();
                        Debug.Log("updateing db logout");
                        command = "UPDATE user SET loginIP = @ip, xp = @xp, rocks = @rocks, bTokens = @bTokens, skin = @skin, powerup = @powerup WHERE id = @id";
                        using (MySqlCommand savePlayerValues = new MySqlCommand(command, sqlConn))
                        {
                            savePlayerValues.Parameters.Add("@id", MySqlDbType.VarChar).Value = id;
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
}
