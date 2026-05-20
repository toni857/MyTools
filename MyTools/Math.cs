namespace My;

public static class Math
{
    public const double m = 1;
    public const double km = 1000;
    public const double s = 1;
    public const double min = 60;
    public const double h = 3600;
    public const double d = 86400;

    public static double Kilometers(double value)
    {
        return value * 1000;
    }

    public static double MetersPerSecond(double value)
    {
        return value;
    }

    public static double KilometersPerHour(double value)
    {
        return value / 3.6;
    }

    public static double ArithmeticValue(double startValue, double difference, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Der Index darf nicht kleiner als 0 sein.");
        }

        return startValue + difference * index;
    }

    public static List<double> Arithmetic(double startValue, double difference, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        List<double> values = [];

        for (int index = 0; index < count; index++)
        {
            values.Add(ArithmeticValue(startValue, difference, index));
        }

        return values;
    }

    public static double HarmonicValue(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Der Index darf nicht kleiner als 0 sein.");
        }

        return 1.0 / (index + 1);
    }

    public static List<double> Harmonic(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        List<double> values = [];

        for (int index = 0; index < count; index++)
        {
            values.Add(HarmonicValue(index));
        }

        return values;
    }

    public static List<double> HarmonicGreaterThan(double limit)
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Die Grenze darf nicht kleiner als 0 sein.");
        }

        List<double> values = [];
        int index = 0;

        while (true)
        {
            double value = HarmonicValue(index);

            if (value <= limit)
            {
                return values;
            }

            values.Add(value);
            index++;
        }
    }

    public static double RecursiveValue(double startValue, Func<double, double> nextValue, int index)
    {
        if (nextValue is null)
        {
            throw new ArgumentNullException(nameof(nextValue));
        }

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Der Index darf nicht kleiner als 0 sein.");
        }

        double value = startValue;

        for (int currentIndex = 0; currentIndex < index; currentIndex++)
        {
            value = nextValue(value);
        }

        return value;
    }

    public static List<double> Recursive(double startValue, Func<double, double> nextValue, int count)
    {
        if (nextValue is null)
        {
            throw new ArgumentNullException(nameof(nextValue));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        List<double> values = [];
        double value = startValue;

        for (int index = 0; index < count; index++)
        {
            values.Add(value);
            value = nextValue(value);
        }

        return values;
    }

    public static List<double> Mixed(int count, params Func<int, double>[] sequences)
    {
        return Mixed(count, index => index % sequences.Length, sequences);
    }

    public static List<double> Mixed(int count, Func<int, int> sequenceSelector, params Func<int, double>[] sequences)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Die Anzahl darf nicht kleiner als 0 sein.");
        }

        if (sequenceSelector is null)
        {
            throw new ArgumentNullException(nameof(sequenceSelector));
        }

        if (sequences.Length == 0)
        {
            throw new ArgumentException("Mindestens eine Folge muss angegeben werden.", nameof(sequences));
        }

        List<double> values = [];

        for (int index = 0; index < count; index++)
        {
            int sequenceIndex = sequenceSelector(index);

            if (sequenceIndex < 0 || sequenceIndex >= sequences.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sequenceSelector), "Die Auswahlregel hat eine nicht vorhandene Folge ausgewählt.");
            }

            Func<int, double> sequence = sequences[sequenceIndex];

            if (sequence is null)
            {
                throw new ArgumentException("Folgen dürfen nicht null sein.", nameof(sequences));
            }

            values.Add(sequence(index));
        }

        return values;
    }

    public static double Distance(double velocity, double travelTime)
    {
        if (velocity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(velocity), "Die Geschwindigkeit in m/s darf nicht kleiner als 0 sein.");
        }

        if (travelTime < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(travelTime), "Die Zeit in Sekunden darf nicht kleiner als 0 sein.");
        }

        return velocity * travelTime;
    }

    public static double DistanceMeters(double velocity, double travelTime)
    {
        return Distance(velocity, travelTime);
    }

    public static double DistanceKilometers(double velocity, double travelTime)
    {
        return Distance(velocity, travelTime) / km;
    }

    public static double TravelTime(double velocity, double distance)
    {
        if (velocity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(velocity), "Die Geschwindigkeit in m/s muss größer als 0 sein.");
        }

        if (distance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), "Die Strecke in Metern darf nicht kleiner als 0 sein.");
        }

        return distance / velocity;
    }

    public static double TravelTimeSeconds(double velocity, double distance)
    {
        return TravelTime(velocity, distance);
    }

    public static int TravelTimeRoundedUp(double velocity, double distance)
    {
        return (int)global::System.Math.Ceiling(TravelTime(velocity, distance));
    }

    public static int TravelTimeSecondsRoundedUp(double velocity, double distance)
    {
        return TravelTimeRoundedUp(velocity, distance);
    }

    public static double TravelTimeMinutes(double velocity, double distance)
    {
        return TravelTime(velocity, distance) / min;
    }

    public static int TravelTimeMinutesRoundedUp(double velocity, double distance)
    {
        return (int)global::System.Math.Ceiling(TravelTimeMinutes(velocity, distance));
    }

    public static double TravelTimeHours(double velocity, double distance)
    {
        return TravelTime(velocity, distance) / h;
    }

    public static int TravelTimeHoursRoundedUp(double velocity, double distance)
    {
        return (int)global::System.Math.Ceiling(TravelTimeHours(velocity, distance));
    }

    public static double TravelTimeDays(double velocity, double distance)
    {
        return TravelTime(velocity, distance) / d;
    }

    public static int TravelTimeDaysRoundedUp(double velocity, double distance)
    {
        return (int)global::System.Math.Ceiling(TravelTimeDays(velocity, distance));
    }

    public static double Velocity(double distance, double travelTime)
    {
        if (distance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), "Die Strecke in Metern darf nicht kleiner als 0 sein.");
        }

        if (travelTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(travelTime), "Die Zeit in Sekunden muss größer als 0 sein.");
        }

        return distance / travelTime;
    }

    public static double VelocityMetersPerSecond(double distance, double travelTime)
    {
        return Velocity(distance, travelTime);
    }

    public static double VelocityKilometersPerHour(double distance, double travelTime)
    {
        return Velocity(distance, travelTime) * 3.6;
    }
}
