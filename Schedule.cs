namespace binChecker;

public class Schedule
{
    public DateTime Waste { get; set; }
    public DateTime Green { get; init; }
    public DateTime Recycle { get; init; }
    public bool Set { get; set; }

    public bool IsRecycle => Recycle < Green;
    public bool IsGreen => !IsRecycle;
}