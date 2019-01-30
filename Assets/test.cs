using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FASTER.core;
using System;
using System.IO;

public class test : MonoBehaviour
{
    private FasterKV<int, int, int, int, Empty, testFunctions> fht;
    private IDevice log;
    long seq = 0;
    string directory = "logs";

    public bool reading;
    public bool continueSession;

    private void Start()
    {
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        log = Devices.CreateLogDevice(directory + "\\hlog");
        fht = new FasterKV<int, int, int, int, Empty, testFunctions>
        (1L << 20, new testFunctions(), new LogSettings { LogDevice = log },
        new CheckpointSettings { CheckpointDir = directory });

        if (!continueSession)
        {
            Guid guid = fht.StartSession();
            File.WriteAllText(directory + @"\session1.txt", guid.ToString());
        }
        else
        {
            string guidText = File.ReadAllText(directory + @"\latestCheckpoint.txt");
            Guid guid = Guid.Parse(guidText);
            fht.Recover(guid); // recover checkpoint

            guidText = File.ReadAllText(directory + @"\session1.txt");
            guid = Guid.Parse(guidText);
            seq = fht.ContinueSession(guid); // recovered seq identifier
        }

        if (!reading) // writing
        {
            for (int j = 0; j < 30; j++) // key == value
            {
                fht.Upsert(ref j, ref j, Empty.Default, seq++);
                if (j % 20 == 0)
                {
                    fht.TakeFullCheckpoint(out Guid token);
                    Debug.Log(token);
                    File.WriteAllText(directory + @"\latestCheckpoint.txt", token.ToString());
                }
                if (j % 10 == 0)
                    fht.CompletePending(false);
                else if (j % 5 == 0)
                    fht.Refresh();              
            }
        }
        else
        {
            for (int j = 0; j < 5; j++) // keys
            {
                int input = 10;
                int output = 0;
                Debug.Log("j = " + j + "read = " + fht.Read(ref j, ref input, ref output, Empty.Default, seq++));                   
            }   
        }
        Debug.Log("Success");
        fht.CompletePending();
        fht.StopSession();
        fht.Dispose();
        log.Close();
    }
}
