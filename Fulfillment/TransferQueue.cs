using Common;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class TransferQueue
    {
        private string queueName = "";
        private IReliableStateManager stateManager;

        public TransferQueue(IReliableStateManager stateManager, string queueName)
        {
            this.stateManager = stateManager;
            this.queueName = queueName;
        }

        public async Task<string> EnqueueAsync(Transfer transfer)
        {
            IReliableConcurrentQueue<Transfer> transfers =
             await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Transfer>>(queueName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                await transfers.EnqueueAsync(tx, transfer);
                await tx.CommitAsync();
            }
            return transfer.Id;
        }

        public async Task<Transfer> DequeueAsync(ITransaction tx)
        {
            IReliableConcurrentQueue<Transfer> transactions =
             await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Transfer>>(queueName);

            Transfer transaction = null;
            var result = await transactions.TryDequeueAsync(tx);
            if (result.HasValue)
            {
                transaction = result.Value;
            }
            return transaction;
        }

        public async Task<long> CountAsync()
        {
            IReliableConcurrentQueue<Transfer> transactions =
                await this.stateManager.GetOrAddAsync<IReliableConcurrentQueue<Transfer>>(queueName);

            return transactions.Count;
        }
    }
}
