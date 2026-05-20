namespace My;

public static class Functions
{
    private static readonly System.Random Rnd = new();

    public static bool RandomBool()
    {
        return Rnd.Next(2) == 0;
    }

    public static string Random(params string[] values)
    {
        if (values.Length == 0)
        {
            throw new ArgumentException("Mindestens ein Wert muss angegeben werden.", nameof(values));
        }

        return values[Rnd.Next(values.Length)];
    }

    public static string Random(params object[] values)
    {
        if (values.Length == 0)
        {
            throw new ArgumentException("Mindestens ein Wert muss angegeben werden.", nameof(values));
        }

        List<string> texts = [];
        List<double> chances = [];

        foreach (object value in values)
        {
            if (value is string text)
            {
                if (chances.Count > 0)
                {
                    throw new ArgumentException("Alle Strings müssen vor den Zahlen stehen.", nameof(values));
                }

                texts.Add(text);
            }
            else if (value is int intChance)
            {
                chances.Add(intChance);
            }
            else if (value is double doubleChance)
            {
                chances.Add(doubleChance);
            }
            else if (value is float floatChance)
            {
                chances.Add(floatChance);
            }
            else
            {
                throw new ArgumentException("Erlaubt sind nur strings und Zahlen.", nameof(values));
            }
        }

        if (texts.Count == 0)
        {
            throw new ArgumentException("Mindestens ein String muss angegeben werden.", nameof(values));
        }

        if (chances.Count > texts.Count)
        {
            throw new ArgumentException("Es gibt mehr Prozentzahlen als Strings.", nameof(values));
        }

        double usedChance = chances.Sum();

        if (usedChance > 100)
        {
            throw new ArgumentException("Die Prozentzahlen dürfen zusammen nicht über 100 sein.", nameof(values));
        }

        int missingChances = texts.Count - chances.Count;

        if (missingChances > 0)
        {
            double restChance = (100 - usedChance) / missingChances;

            for (int i = 0; i < missingChances; i++)
            {
                chances.Add(restChance);
            }
        }

        double randomNumber = Rnd.NextDouble() * 100;
        double currentChance = 0;

        for (int i = 0; i < texts.Count; i++)
        {
            currentChance += chances[i];

            if (randomNumber < currentChance)
            {
                return texts[i];
            }
        }

        return texts[^1];
    }

    public static T Ask<T>(string question)
    {
        return AskUntilValid<T>(question, "Falscher Datentyp.");
    }

    public static T Ask<T>(
        string question,
        T expectedValue,
        string wrongTypeMessage = "Falscher Datentyp.",
        string wrongValueMessage = "Falscher Wert.")
    {
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;

        while (true)
        {
            T value = AskUntilValid<T>(question, wrongTypeMessage);

            if (comparer.Equals(value, expectedValue))
            {
                return value;
            }

            Console.WriteLine(wrongValueMessage);
        }
    }

    public static void Repeat(int count, Action action)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        for (int i = 0; i < count; i++)
        {
            action();
        }
    }

    public static List<T> Repeat<T>(int count, T value)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        List<T> values = [];

        for (int i = 0; i < count; i++)
        {
            values.Add(value);
        }

        return values;
    }

    public static List<T> Repeat<T>(int count, Func<T> action)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        List<T> values = [];

        for (int i = 0; i < count; i++)
        {
            values.Add(action());
        }

        return values;
    }

    public static List<long> Fibonacci(int count, int startIndex = 0)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        if (startIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Das Startglied darf nicht kleiner als 0 sein.");
        }

        List<long> values = [];
        long previous = 0;
        long current = 1;

        for (int index = 0; index < startIndex + count; index++)
        {
            if (index >= startIndex)
            {
                values.Add(previous);
            }

            long next = previous + current;
            previous = current;
            current = next;
        }

        return values;
    }

    private static T AskUntilValid<T>(string question, string wrongTypeMessage)
    {
        while (true)
        {
            Console.WriteLine(question);
            string? input = Console.ReadLine();

            if (TryConvert(input, out T? value))
            {
                return value!;
            }

            Console.WriteLine(wrongTypeMessage);
        }
    }

    private static bool TryConvert<T>(string? input, out T? value)
    {
        Type type = typeof(T);
        Type realType = Nullable.GetUnderlyingType(type) ?? type;

        if (input is null)
        {
            value = default;
            return false;
        }

        try
        {
            if (realType == typeof(string))
            {
                value = (T)(object)input;
                return true;
            }

            if (realType == typeof(char))
            {
                if (input.Length == 1)
                {
                    value = (T)(object)input[0];
                    return true;
                }

                value = default;
                return false;
            }

            if (realType == typeof(bool))
            {
                if (bool.TryParse(input, out bool boolValue))
                {
                    value = (T)(object)boolValue;
                    return true;
                }

                value = default;
                return false;
            }

            if (realType.IsEnum)
            {
                if (Enum.TryParse(realType, input, true, out object? enumValue))
                {
                    value = (T)enumValue;
                    return true;
                }

                value = default;
                return false;
            }

            value = (T)Convert.ChangeType(input, realType);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
