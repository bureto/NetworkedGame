using UnityEngine;

public static class Constants {
    public const int maxClients = 4000;
    public readonly static int[] skinPrices = { 0, 800, 2000 };
    public readonly static int[] powerupPrices = { 0, 1500, 3000, 1500 };
    public readonly static int[] tokenRockPrices = { 2000 };
    public readonly static int[] tokenRockQuantities = { 400 };
    public const int maps = 2;
    public const int gamemodes = 2;
    public const int lobbies = 350;
    public static WaitForSeconds fiveSeconds = new WaitForSeconds(5);
    public static WaitForSeconds oneTenthSecond = new WaitForSeconds(0.1f);
    public static WaitForSeconds oneSecond = new WaitForSeconds(1);
    public static WaitForSeconds twoMinutes = new WaitForSeconds(120);
}
