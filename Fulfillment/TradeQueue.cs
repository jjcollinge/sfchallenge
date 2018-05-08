using Common;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public async Task<string> EnqueueAsync(Trade trade)
        {
            IReliableConcurrentQueue<Trade> trades =
             await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(queueName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                await trades.EnqueueAsync(tx, trade);
                await tx.CommitAsync();
            }
            return trade.Id;
        }

        public async Task<Trade> DequeueAsync(ITransaction tx)
        {
            IReliableConcurrentQueue<Trade> transactions =
             await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(queueName);

            Trade transaction = null;
            var result = await transactions.TryDequeueAsync(tx);
            if (result.HasValue)
            {
                transaction = result.Value;
            }
            return transaction;
        }

        public async Task<long> CountAsync()
        {
            IReliableConcurrentQueue<Trade> transactions =
                await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(queueName);

            return transactions.Count;
        }
    }
}
