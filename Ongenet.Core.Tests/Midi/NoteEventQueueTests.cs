using System.Collections.Concurrent;
using System.Threading.Tasks;
using Ongenet.Core.Audio.Midi;
using Xunit;

namespace Ongenet.Core.Tests.Midi;

public sealed class NoteEventQueueTests
{
    [Fact]
    public void DrainPreservesSingleProducerOrder()
    {
        var queue = new NoteEventQueue<Event>(32);
        for (var i = 0; i < 24; i++) queue.Enqueue(new Event(0, i));

        var drained = queue.Drain();

        Assert.Equal(24, drained.Length);
        for (var i = 0; i < drained.Length; i++) Assert.Equal(i, drained[i].Sequence);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void ConcurrentProducersPublishEveryEventWithinCapacity()
    {
        const int producerCount = 4;
        const int eventsPerProducer = 40;
        var queue = new NoteEventQueue<Event>(producerCount * eventsPerProducer);

        Parallel.For(0, producerCount, producer =>
        {
            for (var sequence = 0; sequence < eventsPerProducer; sequence++)
                queue.Enqueue(new Event(producer, sequence));
        });

        var seen = new ConcurrentDictionary<(int, int), byte>();
        foreach (var item in queue.Drain()) seen.TryAdd((item.Producer, item.Sequence), 0);
        Assert.Equal(producerCount * eventsPerProducer, seen.Count);
    }

    private readonly record struct Event(int Producer, int Sequence);
}
