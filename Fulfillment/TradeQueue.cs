using Common;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class TradeQueue
    {
        private string queueName = "";
        private IReliableStateManager stateManager;

        public TradeQueue(IReliableStateManager stateManager, string queueName)
        {
            this.stateManager = stateManager;
            this.queueName = queueName;
        }

        public async Task<string> EnqueueAsync(Trade trade, CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<Trade> trades =
             await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(queueName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                await trades.EnqueueAsync(tx, trade, cancellationToken);
                await tx.CommitAsync();
            }
            return trade.Id;
        }

        public async Task<Trade> DequeueAsync(ITransaction tx, CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<Trade> transactions =
             await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(queueName);

            Trade trade = null;
            var result = await transactions.TryDequeueAsync(tx, cancellationToken);
            if (result.HasValue)
            {
                trade = result.Value;
            }
            return trade;
        }

        public async Task<long> CountAsync()
        {
            IReliableConcurrentQueue<Trade> transactions =
                await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(queueName);

            return transactions.Count;
        }
    }
}
