using System.Diagnostics;
using ZettelWeb;

namespace ZettelWeb.Tests.Telemetry;

public class ZettelTelemetryTests
{
    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        Assert.Equal("ZettelWeb", ZettelTelemetry.ActivitySource.Name);
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        Assert.Equal("ZettelWeb", ZettelTelemetry.Meter.Name);
    }

    [Fact]
    public void StartActivity_ReturnsActivity_WhenListenerActive()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ZettelWeb",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = ZettelTelemetry.ActivitySource.StartActivity("test.operation");

        Assert.NotNull(activity);
        Assert.Equal("test.operation", activity.DisplayName);
    }
}
