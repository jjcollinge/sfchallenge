using Common;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OrderBook.Tests
{
    public class TestOrderBook_RunAsyncShould : OrderBook
    {
        public TestOrderBook_RunAsyncShould() : base(Helpers.GetMockContext(), new MockReliableStateManager())
        {}

        [Fact]
        public async Task ReturnWithinTimeoutWhenCancelled()
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Task Cancelled Exception will be thrown during Task.Delay(500, cancellationToken);
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await RunAsync(cts.Token));
            watch.Stop();
            var elapsedSec = watch.ElapsedMilliseconds / 1000;

            Assert.True(elapsedSec <= 15);
        }

        [Fact]
        public async Task ReturnWithinTimeoutWhenCancelledAndProcessing()
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var ask = new Order(CurrencyPair.GBPUSD, 100, 200);
            var bid = new Order(CurrencyPair.GBPUSD, 50, 220);
            await this.AddAskAsync(ask);
            await this.AddBidAsync(bid);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Opertation cancelled exception will be thrown by cancellationToken.ThrowIfCancellationRequested();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await RunAsync(cts.Token));
            watch.Stop();
            var elapsedSec = watch.ElapsedMilliseconds / 1000;

            Assert.True(elapsedSec <= 15);
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            return base.RunAsync(cancellationToken);
        }

    }
}
