using System.Net;
using System.IO;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Open.ChannelExtensions;
using System.Linq;

namespace ProducerConsumer
{
    enum Status
    {
        Ok,
        Error
    }

    record Data(Guid Id, DateTime Date);
    record ExternalData(Data Data, Guid[] RelatedIds);
    record SaveResult(Guid[] Ids, Status Status, DateTime Date);

    class Program
    {
        private static ChannelReader<Guid> Producer(CancellationToken cancellationToken = default)
        {
            var chan = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1) { SingleWriter = true, SingleReader = true });
            async Task Produce(ChannelWriter<Guid> writer, CancellationToken token)
            {
                var lines = await File.ReadAllLinesAsync("./data.txt", token);
                foreach (var line in lines)
                {
                    if (Guid.TryParse(line, out Guid guid))
                    {
                        await writer.WriteAsync(guid, token);
                        Console.WriteLine($"Produced guid: {guid}");
                    }

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
            var chan = Channel.CreateBounded<Data>(new BoundedChannelOptions(1) { SingleWriter = true, SingleReader = false });
            async Task Produce(ChannelReader<Guid> stream, ChannelWriter<Data> writer, CancellationToken token)
            {
                await foreach (var id in stream.ReadAllAsync(token))
                {
                    var data = new Data(id, DateTime.UtcNow);
                    Console.WriteLine($"Data ready for processing: {data}");
                    await writer.WriteAsync(data, token);
                }
            }

            Task.Run(async () =>
            {
                try
                {
                    await Produce(guids, chan, cancellationToken);
                    chan.Writer.Complete();
                }
                catch (Exception ex)
                {
                    chan.Writer.Complete(ex);
                }

            }, cancellationToken);
            return chan.Reader;
        }

        static ChannelReader<ExternalData> FetchData(ChannelReader<Data> guids, CancellationToken cancellationToken = default)
        {

            var chan = Channel.CreateBounded<ExternalData>(new BoundedChannelOptions(1) { SingleWriter = false, SingleReader = false });

            static async Task Produce(int conId, ChannelReader<Data> stream, ChannelWriter<ExternalData> writer, CancellationToken token)
            {
                await foreach (var id in stream.ReadAllAsync(token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    var data = new ExternalData(id, Enumerable.Range(1, conId).Select(_ => Guid.NewGuid()).ToArray());
                    Console.WriteLine($"Fetch data from external source {data} in ConsumerId = {conId}");
                    await writer.WriteAsync(data);
                }
            }

            var consume = Enumerable.Range(0, Environment.ProcessorCount).Select(id => Produce(id, guids, chan, cancellationToken));

            Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(consume);
                    chan.Writer.Complete();
                }
                catch (Exception ex)
                {
                    chan.Writer.Complete(ex);
                }

            }, cancellationToken);
            return chan.Reader;
        }

        static ChannelReader<SaveResult> SaveData(ChannelReader<ExternalData> guids, CancellationToken cancellationToken = default)
        {

            var chan = Channel.CreateBounded<SaveResult>(new BoundedChannelOptions(7) { SingleWriter = true, SingleReader = true });

            static ValueTask<SaveResult> SaveToDb(List<ExternalData> externals)
            {
                return new ValueTask<SaveResult>(new SaveResult(externals.Select(x => x.Data.Id).ToArray(), Status.Ok, DateTime.Now));
            }

            static async Task Save(ChannelReader<ExternalData> stream, ChannelWriter<SaveResult> writer, CancellationToken token)
            {
                const int batchSize = 3;
                var data = new List<ExternalData>(batchSize);
                await foreach (var externalData in stream.ReadAllAsync(token))
                {
                    if (data.Count < batchSize)
                    {
                        data.Add(externalData);
                    }
                    else
                    {
                        await writer.WriteAsync(await SaveToDb(data), token);
                        data.Clear();
                    }
                }
                if (data.Count > 0)
                {
                    await writer.WriteAsync(await SaveToDb(data), token);
                }

                writer.Complete();
            }

            Task.Run(async () =>
            {
                try
                {
                    await Save(guids, chan, cancellationToken);
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
            var prod = SaveData(FetchData(PrepareData(Producer())));

            await foreach (var id in prod.ReadAllAsync())
            {
                Console.WriteLine(id);
            }
        }
    }
}
