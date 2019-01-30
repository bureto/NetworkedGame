using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SumStore;

public class monoProgram : MonoBehaviour
{
    public string[] args;

    // Start is called before the first frame update
    void Start()
    {
        UnitySystemConsoleRedirector.Redirect();
        Program.Main(args);
    }
}
