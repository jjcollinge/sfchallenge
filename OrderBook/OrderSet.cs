using Common;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Data.Notifications;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrderBook
{
    /// <summary>
    /// An OrderSet keeps track of a set of orders using a reliable dictionary.
    /// It uses a secondary index to keep track of the ordering of the 
    /// dictionary keys. This allows us to select the maximum or minimum value
    /// orders.
    /// </summary>
    public class OrderSet
    {
        private string setName = "";
        private IReliableStateManager stateManager;

        // Used to keep an ordered reference of the dictionary keys.
        // Stores keys in ascending order.
        // Note: This is public for testing purposes as the required
        // mock reliable collection notifications are not implemented.
        public ImmutableSortedSet<Order> SecondaryIndex;

        public OrderSet(IReliableStateManager stateManager, string setName)
        {
            this.SecondaryIndex = ImmutableSortedSet.Create<Order>();

            this.stateManager = stateManager;
            this.setName = setName;
            this.stateManager.StateManagerChanged += this.OnStateManagerChangedHandler;
        }

        /// <summary>
        /// Enumerates through the dictionary and
        /// returns a list of all the key value
        /// pairs.
        /// </summary>
        /// <returns></returns>
        public async Task<List<KeyValuePair<string, Order>>> GetOrdersAsync()
        {
            List<KeyValuePair<string, Order>> result = new List<KeyValuePair<string, Order>>();

            ConditionalValue<IReliableDictionary<string, Order>> conditionResult =
                await this.stateManager.TryGetAsync<IReliableDictionary<string, Order>>(this.setName);

            if (conditionResult.HasValue)
            {
                IReliableDictionary<string, Order> orders = conditionResult.Value;

                using (var tx = this.stateManager.CreateTransaction())
                {
                    Microsoft.ServiceFabric.Data.IAsyncEnumerable<KeyValuePair<string, Order>> enumerable = await orders.CreateEnumerableAsync(tx);
                    Microsoft.ServiceFabric.Data.IAsyncEnumerator<KeyValuePair<string, Order>> enumerator = enumerable.GetAsyncEnumerator();

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
        /// Adds an order to the dictionary
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task AddOrderAsync(Order order)
        {
            IReliableDictionary<string, Order> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Order>>(this.setName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await orders.TryAddAsync(tx, order.Id, order);
                await tx.CommitAsync();
            }
            return;
        }

        /// <summary>
        /// Clears all orders from dictionary
        /// </summary>
        /// <returns></returns>
        public async Task ClearAsync()
        {
            IReliableDictionary<string, Order> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Order>>(this.setName);

            this.SecondaryIndex.Clear();
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await orders.ClearAsync();
            }
        }

        /// <summary>
        /// Gets an order by order id
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private async Task<Order> GetOrderAsync(string id)
        {
            IReliableDictionary<string, Order> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Order>>(this.setName);

            Order order = null;
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                var result = await orders.TryGetValueAsync(tx, id);
                if (result.HasValue)
                {
                    order = result.Value;
                }
                await tx.CommitAsync();
            }
            return order;
        }

        /// <summary>
        /// Gets the max order in the dictionary
        /// </summary>
        /// <returns></returns>
        public Order GetMaxOrder()
        {
            return this.SecondaryIndex.LastOrDefault();
        }

        /// <summary>
        /// Gets the min order in the dictionary
        /// </summary>
        /// <returns></returns>
        public Order GetMinOrder()
        {
            return this.SecondaryIndex.FirstOrDefault();
        }

        /// <summary>
        /// Gets the order count for the dictionary
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<long> CountAsync()
        {
            IReliableDictionary<string, Order> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Order>>(this.setName);

            long count = 0;
            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                count = await orders.GetCountAsync(tx);
                await tx.CommitAsync();
            }
            return count;
        }

        /// <summary>
        /// Removes an order from the dictionary
        /// and the secondary index
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task<bool> RemoveAsync(Order order)
        {
            IReliableDictionary<string, Order> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Order>>(this.setName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                var result = await orders.TryRemoveAsync(tx, order.Id);
                if (result.HasValue)
                {
                    lock (this.SecondaryIndex)
                    {
                        this.SecondaryIndex = this.SecondaryIndex.Remove(order);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes an order from the dictionary
        /// and secondary index. Uses existing
        /// transaction that will be handled by
        /// the caller.
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task<bool> RemoveAsync(ITransaction tx, Order order)
        {
            IReliableDictionary<string, Order> orders =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Order>>(this.setName);

            var result = await orders.TryRemoveAsync(tx, order.Id);
            if (result.HasValue)
            {
                lock (this.SecondaryIndex)
                {
                    this.SecondaryIndex = this.SecondaryIndex.Remove(order);
                }
                return true;
            }
            return false;
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

            if (entity.ReliableState is IReliableDictionary<string, Order>)
            {
                var dictionary = (IReliableDictionary<string, Order>)entity.ReliableState;
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
        private void OnDictionaryChangedHandler(object sender, NotifyDictionaryChangedEventArgs<string, Order> e)
        {
            if (e.Action == NotifyDictionaryChangedAction.Add)
            {
                var addEvent = e as NotifyDictionaryItemAddedEventArgs<string, Order>;
                this.ProcessDictionaryAddNotification(addEvent);
                return;
            }
            if (e.Action == NotifyDictionaryChangedAction.Update)
            {
                var updateEvent = e as NotifyDictionaryItemUpdatedEventArgs<string, Order>;
                this.ProcessDictionaryUpdateNotification(updateEvent);
                return;
            }
            if (e.Action == NotifyDictionaryChangedAction.Remove)
            {
                var removeEvent = e as NotifyDictionaryItemRemovedEventArgs<string, Order>;
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
             IReliableDictionary<string, Order> origin,
             NotifyDictionaryRebuildEventArgs<string, Order> rebuildNotification)
        {
            this.SecondaryIndex.Clear();

            var enumerator = rebuildNotification.State.GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync(CancellationToken.None))
            {
                lock (this.SecondaryIndex)
                {
                    this.SecondaryIndex = this.SecondaryIndex.Add(enumerator.Current.Value);
                }
            }
        }

        /// <summary>
        /// Called when a dictionary item add notification has been
        /// triggered. Add the new key to our secondary index.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessDictionaryAddNotification(NotifyDictionaryItemAddedEventArgs<string, Order> e)
        {
            if (e?.Value != null)
            {
                lock (this.SecondaryIndex)
                {
                    this.SecondaryIndex = this.SecondaryIndex.Add(e.Value);
                }
            }
        }

        /// <summary>
        /// Called when a dictionary item update notification has been
        /// triggered. Add the key to our secondary index if we 
        /// don't have it.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessDictionaryUpdateNotification(NotifyDictionaryItemUpdatedEventArgs<string, Order> e)
        {
            if (e?.Value != null)
            {
                lock (this.SecondaryIndex)
                {
                    this.SecondaryIndex = this.SecondaryIndex.Add(e.Value);
                }
            }
        }

        /// <summary>
        /// Called when a dictionary item remove notification has been
        /// triggered. Remove the key from our secondary index.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessDictionaryRemoveNotification(NotifyDictionaryItemRemovedEventArgs<string, Order> e)
        {
            var order = this.SecondaryIndex.Where(x => x.Id == e.Key).FirstOrDefault();
            if (order != null)
            {
                lock (this.SecondaryIndex)
                {
                    this.SecondaryIndex = this.SecondaryIndex.Remove(order);
                }
            }
        }
    }
}
