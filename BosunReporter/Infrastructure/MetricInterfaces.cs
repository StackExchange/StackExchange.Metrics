namespace BosunReporter.Infrastructure
{
    public interface IDoubleGauge
    {
        void Record(double value);
    }

    public interface ILongGauge
    {
        void Record(long value);
    }

    public interface IIntGauge
    {
        void Record(int value);
    }

    public interface IDoubleCounter
    {
        void Increment(double amount = 1.0);
    }

    public interface ILongCounter
    {
        void Increment(long amount = 1);
    }

    public interface IIntCounter
    {
        void Increment(int amount = 1);
    }
}
