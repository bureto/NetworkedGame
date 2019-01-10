using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public class ByteBuffer1 : IDisposable
{
    List<byte> Buff;
    byte[] readBuff;
    int readpos;
    bool buffUpdate = false;

    public ByteBuffer1()
    {
        Buff = new List<byte>();
        readpos = 0;
    }

    public int GetReadPos()
    {
        return readpos;
    }

    public byte[] ToArray()
    {
        return Buff.ToArray();
    }

    public int Count()
    {
        return Buff.Count;
    }

    public int Length()
    {
        return Count() - readpos;
    }

    public void Clear()
    {
        Buff.Clear();
        readpos = 0;
    }
    #region "Write Data"
    public void WriteByte(byte Inputs)
    {
        Buff.Add(Inputs);
        buffUpdate = true;
    }

    public void WriteBytes(byte[] Input)
    {
        Buff.AddRange(Input);
        buffUpdate = true;
    }

    public void WriteShort(short Input)
    {
        Buff.AddRange(BitConverter.GetBytes(Input));
        buffUpdate = true;
    }

    public void WriteInteger(int Input)
    {
        Buff.AddRange(BitConverter.GetBytes(Input));
        buffUpdate = true;
    }

    public void WriteFLoat(float Input)
    {
        Buff.AddRange(BitConverter.GetBytes(Input));
        buffUpdate = true;
    }

    public void WriteString(string Input)
    {
        Buff.AddRange(BitConverter.GetBytes(Input.Length));
        Buff.AddRange(Encoding.ASCII.GetBytes(Input));
        buffUpdate = true;
    }

    public void WriteVector2(Vector2 Input)
    {
        Buff.AddRange(BitConverter.GetBytes(Input.x));
        Buff.AddRange(BitConverter.GetBytes(Input.y));
        buffUpdate = true;
    }

    public void WriteVector3(Vector3 Input)
    {
        Buff.AddRange(BitConverter.GetBytes(Input.x));
        Buff.AddRange(BitConverter.GetBytes(Input.y));
        Buff.AddRange(BitConverter.GetBytes(Input.z));
        buffUpdate = true;
    }
    #endregion

    #region "Read Data"

    public byte ReadByte(bool Peek = true)
    {
        if (Buff.Count > readpos)
        {
            if (buffUpdate)
            {
                readBuff = Buff.ToArray();
                buffUpdate = false;
            }

            byte ret = readBuff[readpos];
            if (Peek & Buff.Count > readpos)
            {
                readpos += 1;
            }
            return ret;
        }

        else
        {
            throw new Exception("Byte Buffer Past Limit!");
        }
    }

    public byte[] ReadBytes(int length, bool Peek = true)
    {
        if (buffUpdate)
        {
            readBuff = Buff.ToArray();
            buffUpdate = false;
        }

        byte[] ret = Buff.GetRange(readpos, length).ToArray();
        if (Peek)
        {
            readpos += length;
        }

        return ret;
    }

    public int ReadInteger(bool Peek = true)
    {
        if (Buff.Count > readpos)
        {
            if (buffUpdate)
            {
                readBuff = Buff.ToArray();
                buffUpdate = false;
            }

            int ret = BitConverter.ToInt32(readBuff, readpos);
            if (Peek == true & Buff.Count > readpos)
            {
                readpos += 4; //32bit int = 4 bytes
            }
            return ret;
        }

        else
        {
            throw new Exception("Byte Buffer is Past its Limit!");
        }
    }

    public float ReadFloat(bool Peek = true)
    {
        if (Buff.Count > readpos)
        {
            if (buffUpdate)
            {
                readBuff = Buff.ToArray();
                buffUpdate = false;
            }

            float ret = BitConverter.ToSingle(readBuff, readpos);
            if (Peek == true & Buff.Count > readpos)
            {
                readpos += 4; //packetname contains int, add 4 so do not read it again
            }
            return ret;
        }

        else
        {
            throw new Exception("Byte Buffer is Past its Limit!");
        }
    }

    public string ReadString(bool Peek = true)
    {
        int len = ReadInteger(true); //we send length of string each time we send a string, this reads it.
        if (buffUpdate)
        {
            readBuff = Buff.ToArray();
            buffUpdate = false;
        }

        string ret = Encoding.ASCII.GetString(readBuff, readpos, len);
        if (Peek == true & Buff.Count > readpos)
        {
            if (ret.Length > 0)
            {
                readpos += len;
            }
        }
        return ret;
    }

    public Vector2 ReadVector2(bool Peek = true)
    {
        if (Buff.Count > readpos)
        {
            if (buffUpdate)
            {
                readBuff = Buff.ToArray();
                buffUpdate = false;
            }

            Vector2 ret = new Vector2(ReadFloat(), ReadFloat());
            return ret;
        }

        else
        {
            throw new Exception("Byte Buffer is Past its Limit!");
        }
    }

    public Vector3 ReadVector3(bool Peek = true)
    {
        if (Buff.Count > readpos)
        {
            if (buffUpdate)
            {
                readBuff = Buff.ToArray();
                buffUpdate = false;
            }

            Vector3 ret = new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
            return ret;
        }

        else
        {
            throw new Exception("Byte Buffer is Past its Limit!");
        }
    }

    #endregion

    private bool disposedValue = false;

    //IDisposable
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposedValue)
        {
            if (disposing)
            {
                Buff.Clear();
            }

            readpos = 0;
        }
        this.disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

