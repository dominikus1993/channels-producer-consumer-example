using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ProducerConsumer
{
    interface IProducer<T>
    {
        ChannelReader<T> Produce();
    }

    class NumberProducer : IProducer<List<int>>
    {
        private Channel<List<int>> _channel;

        public NumberProducer(Channel<List<int>> channel)
        {
            _channel = channel;
        }

        public ChannelReader<List<int>> Produce()
        {
            const int prodCount = 10;
            var producers = new List<Task>(prodCount);
            for (var i = 0; i < prodCount; i++)
            {
                producers.Add(ProduceNumbers(_channel));
            }

            Task.Run(async () =>
            {
                await Task.WhenAll(producers);
                _channel.Writer.Complete();
            });

            return _channel.Reader;
        }

        private async Task ProduceNumbers(ChannelWriter<List<int>> writer)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            await writer.WriteAsync(new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var prod = new NumberProducer(Channel.CreateBounded<List<int>>(10));

            await foreach (var i in prod.Produce().ReadAllAsync())
            {
                Console.WriteLine(string.Join(", ", i));
            }
        }
    }
}
