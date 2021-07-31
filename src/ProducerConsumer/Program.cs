using System.Net;
using System.IO;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Open.ChannelExtensions;
namespace ProducerConsumer
{

    record Data(Guid Id, DateTime Date);
    class Program
    {
        private static ChannelReader<Guid> Producer(CancellationToken cancellationToken = default)
        {
            var chan = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1) { SingleWriter = false, SingleReader = false });
            async Task Produce(ChannelWriter<Guid> writer, CancellationToken token)
            {
                var lines = await File.ReadAllLinesAsync("./data.txt");
                foreach (var line in lines)
                {
                    if (Guid.TryParse(line, out Guid guid))
                        await writer.WriteAsync(guid);
                }
            }

            Task.Run(async () =>
            {
                try
                {
                    await Produce(chan, cancellationToken);
                    chan.Writer.Complete();
                }
                catch (Exception ex)
                {
                    chan.Writer.Complete(ex);
                }

            }, cancellationToken);
            return chan.Reader;
        }

        static ChannelReader<Data> PrepareData(ChannelReader<Guid> guids, CancellationToken cancellationToken = default)
        {
            var chan = Channel.CreateBounded<Data>(new BoundedChannelOptions(1) { SingleWriter = false, SingleReader = false });
            async Task Produce(ChannelWriter<Data> writer, CancellationToken token)
            {
                await foreach(var id in guids.ReadAllAsync()) {
                    
                }
            }

            Task.Run(async () =>
            {
                try
                {
                    await Produce(chan, cancellationToken);
                    chan.Writer.Complete();
                }
                catch (Exception ex)
                {
                    chan.Writer.Complete(ex);
                }

            }, cancellationToken);
            return chan.Reader;
        }
        static async Task Main(string[] args)
        {
            var prod = Producer();

            await foreach (var id in prod.ReadAllAsync())
            {
                Console.WriteLine(id);
            }
        }
    }
}
