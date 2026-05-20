namespace My;

public static class Console
{
    public static int BufferHeight => System.Console.BufferHeight;
    public static int BufferWidth => System.Console.BufferWidth;
    public static bool IsOutputRedirected => System.Console.IsOutputRedirected;
    public static int LargestWindowHeight => System.Console.LargestWindowHeight;
    public static int LargestWindowWidth => System.Console.LargestWindowWidth;
    public static int WindowHeight => System.Console.WindowHeight;
    public static int WindowWidth => System.Console.WindowWidth;

    public static void Line()
    {
        System.Console.WriteLine();
    }

    public static void LineWrite(object? value)
    {
        System.Console.WriteLine();
        System.Console.Write(value);
    }

    public static void Clear()
    {
        System.Console.Clear();
    }

    public static ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        return System.Console.ReadKey(intercept);
    }

    public static string? ReadLine()
    {
        return System.Console.ReadLine();
    }

    public static void SetBufferSize(int width, int height)
    {
        System.Console.SetBufferSize(width, height);
    }

    public static void SetCursorPosition(int left, int top)
    {
        System.Console.SetCursorPosition(left, top);
    }

    public static void SetWindowSize(int width, int height)
    {
        System.Console.SetWindowSize(width, height);
    }

    public static void Write(object? value)
    {
        System.Console.Write(value);
    }

    public static void Write(string? value)
    {
        System.Console.Write(value);
    }

    public static void WriteLine()
    {
        System.Console.WriteLine();
    }

    public static void WriteLine(object? value)
    {
        System.Console.WriteLine(value);
    }

    public static void WriteLine(string? value)
    {
        System.Console.WriteLine(value);
    }
}
