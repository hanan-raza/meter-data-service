using MeterDataService.Application.Import;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class ImportChannelTests
{
    [Fact]
    public void Full_channel_refuses_instead_of_growing()
    {
        var channel = new ImportChannel(capacity: 2);

        channel.TryEnqueue(Job()).ShouldBeTrue();
        channel.TryEnqueue(Job()).ShouldBeTrue();
        channel.TryEnqueue(Job()).ShouldBeFalse();

        channel.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Jobs_are_read_in_the_order_they_were_queued()
    {
        var channel = new ImportChannel();
        var first = Job();
        var second = Job();
        channel.TryEnqueue(first);
        channel.TryEnqueue(second);

        (await channel.Reader.ReadAsync()).ShouldBe(first);
        (await channel.Reader.ReadAsync()).ShouldBe(second);
        channel.Count.ShouldBe(0);
    }

    [Fact]
    public void Default_capacity_holds_one_hundred_jobs()
    {
        var channel = new ImportChannel();

        for (var i = 0; i < ImportChannel.DefaultCapacity; i++)
        {
            channel.TryEnqueue(Job()).ShouldBeTrue();
        }

        channel.TryEnqueue(Job()).ShouldBeFalse();
    }

    internal static ImportJob Job(string content = "timestamp,value,status\n") =>
        new(Guid.CreateVersion7(), "41373559241", "day.csv", content, new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero));
}
