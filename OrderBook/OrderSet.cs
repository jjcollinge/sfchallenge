using Common;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Data.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrderBook
{
    /// <summary>
    /// An OrderSet keeps track of a set of orders using a reliable dictionary.
    /// It uses the order value as a key and a generic C# queue of orders as 
    /// a value. It then uses a secondary index to keep track of the ordering
    /// of the dictionary keys. This allows us to achieve price-time ordering
    /// across our orders.
    /// We can select the maximum or minimum value order queue and then pop
    /// off the oldest order at that amount.
    /// </summary>
    public class OrderSet
    {
        private string setName = "";
        private IReliableStateManager stateManager;

        // Used to keep an ordered reference of the dictionary keys.
        // Stores keys in ascending order.
        private SortedSet<int> secondaryIndex;

        public OrderSet(IReliableStateManager stateManager, string setName)
        {
            this.stateManager = stateManager;
            this.setName = setName;
            this.secondaryIndex = new SortedSet<int>();

            this.stateManager.StateManagerChanged += this.OnStateManagerChangedHandler;
        }

        /// <summary>
        /// Enumerates through the dictionary and
        /// returns a list of all the key value
        /// pairs.
        /// </summary>
        /// <returns></returns>
        public async Task<List<KeyValuePair<int, Queue<Order>>>> GetOrdersAsync()
        {
            List<KeyValuePair<int, Queue<Order>>> result = new List<KeyValuePair<int, Queue<Order>>>();

            ConditionalValue<IReliableDictionary<int, Queue<Order>>> conditionResult =
                await this.stateManager.TryGetAsync<IReliableDictionary<int, Queue<Order>>>(this.setName);

            if (conditionResult.HasValue)
            {
                IReliableDictionary<int, Queue<Order>> orders = conditionResult.Value;

                using (var tx = this.stateManager.CreateTransaction())
                {
                    Microsoft.ServiceFabric.Data.IAsyncEnumerable<KeyValuePair<int, Queue<Order>>> enumerable = await orders.CreateEnumerableAsync(tx);
                    Microsoft.ServiceFabric.Data.IAsyncEnumerator<KeyValuePair<int, Queue<Order>>> enumerator = enumerable.GetAsyncEnumerator();

                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                    {
                        result.Add(enumerator.Current);
                    }
                    await tx.CommitAsync();
                }
            }
            return result;
        }

        /// <summary>
        /// Checks whether an existing queue exists
        /// for the given key. If it doesn't, it creates 
        /// a new queue, if it does it uses it. It then
        /// pushes the new order onto the queue and
        /// writes the queue back into the dictionary.
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task AddOrderAsync(Order order)
        {
            IReliableDictionary<int, Queue<Order>> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Queue<Order>>>(this.setName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                var tryOrderQueue = await orders.TryGetValueAsync(tx, (int)order.Value);

                Queue<Order> orderQueue;
                if (tryOrderQueue.HasValue)
                {
                    orderQueue = tryOrderQueue.Value;
                }
                else
                {
                    orderQueue = new Queue<Order>();
                }
                orderQueue.Enqueue(order);
                await orders.SetAsync(tx, (int)order.Value, orderQueue);
                await tx.CommitAsync();
            }
            return;
        }

        /// <summary>
        /// Gets the maximum key from the ordered secondary
        /// index and then attempts to peek the last order
        /// in its order queue.
        /// </summary>
        /// <returns></returns>
        public async Task<Order> PeekMaxOrderAsync()
        {
            var maxKey = this.secondaryIndex.LastOrDefault();
            if (maxKey == default(int))
            {
                return null;
            }
            return await GetOrderByKeyAsync(maxKey);
        }

        /// <summary>
        /// Gets the minimum key from the ordered secondary
        /// index and then attempts to peek the first order
        /// in its order queue.
        /// </summary>
        /// <returns></returns>
        public async Task<Order> PeekMinOrderAsync()
        {
            var minKey = this.secondaryIndex.FirstOrDefault();
            if (minKey == default(int))
            {
                return null;
            }
            return await GetOrderByKeyAsync(minKey);
        }

        /// <summary>
        /// Checks whether an order is the first in
        /// the order queue for its value. If it is
        /// it dequeues it.
        /// This is assumed to be run after peeking
        /// a value as a method of removing the
        /// order from the queue.
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task ResolveOrderAsync(ITransaction tx, Order order)
        {
            IReliableDictionary<int, Queue<Order>> orders =
               await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Queue<Order>>>(this.setName);

            var tryQueue = await orders.TryGetValueAsync(tx, (int)order.Value);
            if (tryQueue.HasValue)
            {
                var queue = tryQueue.Value;
                var first = queue.Peek();
                if (first.Id != order.Id)
                {
                    throw new Exception($"cannot match order {order.Id} as it is no longer at the front of the queue");
                }
                queue.Dequeue();
                if (queue.Count == 0)
                {
                    var removed = await orders.TryRemoveAsync(tx, (int)order.Value);
                    if (!removed.HasValue)
                    {
                        throw new Exception($"failed to remove key {order.Value} when queue empty");
                    }
                }
            }
        }

        /// <summary>
        /// Grabs the first order in an order queue
        /// for a given key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private async Task<Order> GetOrderByKeyAsync(int key)
        {
            IReliableDictionary<int, Queue<Order>> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Queue<Order>>>(this.setName);

            Order order;
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                if(!await orders.ContainsKeyAsync(tx, key))
                {
                   throw new Exception($"Desired key '{key}' does not exist in dictionary");
                }

                var tryValue = await orders.TryGetValueAsync(tx, key);
                if (tryValue.HasValue)
                {
                    var value = tryValue.Value;
                    if (value.Count == 0)
                    {
                        throw new Exception($"key '{key}' exists but contains empty queue");
                    }
                    order = value.Peek();
                }
                else
                {
                    throw new Exception($"key '{key}' exists but failed to get it");
                }
                await tx.CommitAsync();
            }
            return order;
        }

        /// <summary>
        /// Gets the queue depth for a given key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<int> GetCountForKey(int key)
        {
            IReliableDictionary<int, Queue<Order>> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<int, Queue<Order>>>(this.setName);

            int depth;
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                if (!await orders.ContainsKeyAsync(tx, key))
                {
                    throw new Exception($"Desired key '{key}' does not exist in dictionary");
                }

                var tryValue = await orders.TryGetValueAsync(tx, key);
                if (tryValue.HasValue)
                {
                    var value = tryValue.Value;
                    depth = value.Count;
                }
                else
                {
                    throw new Exception($"key '{key}' exists but failed to get it");
                }
                await tx.CommitAsync();
            }
            return depth;
        }

        /// <summary>
        /// Called in response to a change on the state manager 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnStateManagerChangedHandler(object sender, NotifyStateManagerChangedEventArgs e)
        {
            if (e.Action == NotifyStateManagerChangedAction.Add)
            {
                this.ProcessStateManagerAddNotification(e);
                return;
            }
        }

        /// <summary>
        /// Called in response to an add change notification on the 
        /// state manager. This indicates that a collection has been
        /// added. We use this to set our dictionary level callbacks
        /// and notification handlers.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessStateManagerAddNotification(NotifyStateManagerChangedEventArgs e)
        {
            var entity = e as NotifyStateManagerSingleEntityChangedEventArgs;

            if (entity.ReliableState is IReliableDictionary<int, Queue<Order>>)
            {
                var dictionary = (IReliableDictionary<int, Queue<Order>>)entity.ReliableState;
                if (dictionary.Name.LocalPath == this.setName)
                {
                    dictionary.RebuildNotificationAsyncCallback = this.OnDictionaryRebuildNotificationHandlerAsync;
                    dictionary.DictionaryChanged += this.OnDictionaryChangedHandler;
                }
            }
        }

        /// <summary>
        /// Called when the dictionary change notification is triggered
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDictionaryChangedHandler(object sender, NotifyDictionaryChangedEventArgs<int, Queue<Order>> e)
        {
            if (e.Action == NotifyDictionaryChangedAction.Add)
            {
                var addEvent = e as NotifyDictionaryItemAddedEventArgs<int, Queue<Order>>;
                this.ProcessDictionaryAddNotification(addEvent);
                return;
            }
            if (e.Action == NotifyDictionaryChangedAction.Update)
            {
                var updateEvent = e as NotifyDictionaryItemUpdatedEventArgs<int, Queue<Order>>;
                this.ProcessDictionaryUpdateNotification(updateEvent);
                return;
            }
            if (e.Action == NotifyDictionaryChangedAction.Remove)
            {
                var removeEvent = e as NotifyDictionaryItemRemovedEventArgs<int, Queue<Order>>;
                this.ProcessDictionaryRemoveNotification(removeEvent);
                return;
            }

        }

        /// <summary>
        /// Called when the dictionary rebuild notification is triggered.
        /// We use this to rebuild our secondary index.
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="rebuildNotification"></param>
        /// <returns></returns>
        private async Task OnDictionaryRebuildNotificationHandlerAsync(
             IReliableDictionary<int, Queue<Order>> origin,
             NotifyDictionaryRebuildEventArgs<int, Queue<Order>> rebuildNotification)
        {
            this.secondaryIndex.Clear();

            var enumerator = rebuildNotification.State.GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync(CancellationToken.None))
            {
                this.secondaryIndex.Add(enumerator.Current.Key);
            }
        }

        /// <summary>
        /// Called when a dictionary item add notification has been
        /// triggered. Add the new key to our secondary index.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessDictionaryAddNotification(NotifyDictionaryItemAddedEventArgs<int, Queue<Order>> e)
        {
            if (!this.secondaryIndex.Contains(e.Key))
            {
                this.secondaryIndex.Add(e.Key);
            }
        }

        /// <summary>
        /// Called when a dictionary item update notification has been
        /// triggered. Add the key to our secondary index if we 
        /// don't have it.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessDictionaryUpdateNotification(NotifyDictionaryItemUpdatedEventArgs<int, Queue<Order>> e)
        {
            if (!this.secondaryIndex.Contains(e.Key))
            {
                this.secondaryIndex.Add(e.Key);
            }
        }

        /// <summary>
        /// Called when a dictionary item remove notification has been
        /// triggered. Remove the key from our secondary index.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessDictionaryRemoveNotification(NotifyDictionaryItemRemovedEventArgs<int, Queue<Order>> e)
        {
            if (this.secondaryIndex.Contains(e.Key))
            {
                this.secondaryIndex.Remove(e.Key);
            }
        }
    }
}
