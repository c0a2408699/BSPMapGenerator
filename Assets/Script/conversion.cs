using UnityEngine;

public static class conversion
{
    public static int packInt(int a, int b, int c)
    {
        // aを22ビット左へ、bを12ビット左へシフトし、cと結合する
        return (a << 22) | (b << 12) | (c & 0xFFF);
    }

    public static void unpackInt(int packed, out int a, out int b, out int c)
    {
        a = (packed >> 22) & 0x3FF; // 10ビット分のマスク
        b = (packed >> 12) & 0x3FF; // 10ビット分のマスク
        c = packed & 0xFFF;         // 12ビット分のマスク
    }
}
