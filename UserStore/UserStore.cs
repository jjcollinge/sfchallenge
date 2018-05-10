using System.Collections.Generic;
using System.Fabric;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using UserStore.Interface;
using Common;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Threading;
using Microsoft.ServiceFabric.Data;
using System.Fabric.Description;
using System.IO;

namespace UserStore
{
    /// <summary>
    /// A statefull service, used to store Users. Access via binary remoting based on the V2 Remoting stack
    /// </summary>
    public sealed class UserStore : StatefulService, IUserStore
    {
        public const string StateManagerKey = "UserStore";

        // Backup
        private IBackupStore backupStore;
        private BackupManagerType backupStorageType;
        private const string BackupCountDictionaryName = "BackupCountingDictionary";


        public UserStore(StatefulServiceContext context)
            : base(context)
        { }
        public UserStore(StatefulServiceContext context, IReliableStateManagerReplica2 reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        { }

        /// <summary>
        /// Standard implementation for service endpoints using the V2 Remoting stack. See more: https://aka.ms/servicefabricservicecommunication
        /// </summary>
        /// <remarks>
        /// Iterates over the ServiceManifest.xml and expects a endpoint with name ServiceEndpointV2 to expose the service over Remoting.
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        public async Task<User> GetUserAsync(string userId)
        {
            IReliableDictionary<string, User> users =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            User user = null;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var tryUser = await users.TryGetValueAsync(tx, userId);
                if (tryUser.HasValue)
                {
                    user = tryUser.Value;
                }
                await tx.CommitAsync();
            }
            return user;
        }

        public async Task<List<User>> GetUsersAsync()
        {
            IReliableDictionary<string, User> users =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var maxExecutionTime = TimeSpan.FromSeconds(30D); // Stop retrieving users after 30 sec
            var cancellationToken = new CancellationTokenSource(maxExecutionTime).Token;

            var returnList = new List<User>();

            using (var tx = this.StateManager.CreateTransaction())
            {
                var asyncEnumerable = await users.CreateEnumerableAsync(tx);
                var asyncEnumerator = asyncEnumerable.GetAsyncEnumerator();

                try
                {
                    while (await asyncEnumerator.MoveNextAsync(cancellationToken))
                    {
                        returnList.Add(asyncEnumerator.Current.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }

                await tx.CommitAsync();
            }
            return returnList;
        }

        public async Task<string> AddUserAsync(User user)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            using (var tx = this.StateManager.CreateTransaction())
            {
                var current = await users.TryGetValueAsync(tx, user.Id, LockMode.Update);
                if (current.HasValue)
                {
                    await ExecuteUserUpdate(user, users, tx);
                    return user.Id;
                }
                await users.AddAsync(tx, user.Id, user);
                await tx.CommitAsync();
            }
            return user.Id;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            using (var tx = this.StateManager.CreateTransaction())
            {
                return await ExecuteUserUpdate(user, users, tx);
            }
        }

        private static async Task<bool> ExecuteUserUpdate(User user, IReliableDictionary<string, User> users, ITransaction tx)
        {
            bool result;
            var current = await users.TryGetValueAsync(tx, user.Id, LockMode.Update);
            if (current.HasValue)
            {
                result = await users.TryUpdateAsync(tx, user.Id, user, current.Value);
                await tx.CommitAsync();
            }
            else
            {
                throw new ApplicationException($"Cannot update non existent user '{user.Id}'");
            }

            return result;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            bool removed;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var result = await users.TryRemoveAsync(tx, userId);
                if (result.HasValue)
                {
                    removed = true;
                }
                else
                {
                    removed = false;
                }
                await tx.CommitAsync();
            }
            return removed;
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "Running backup of user store periodically");
            return this.PeriodicTakeBackupAsync(cancellationToken);
        }

        #region Backup and Restore

        /// <summary>
        /// OnDataLossAsync is invoked when a restore is occurs.
        /// 
        /// OnDataLossAsync testing can be triggered via powershell. To do so, run the following commands as a script:
        /// Connect-ServiceFabricCluster
        /// $s = "fabric:/WebReferenceApplication/InventoryService"
        /// $p = Get-ServiceFabricApplication | Get-ServiceFabricService -ServiceName $s | Get-ServiceFabricPartition | Select -First 1
        /// $p | Invoke-ServiceFabricPartitionDataLoss -DataLossMode FullDataLoss -ServiceName $s
        /// </summary>
        /// <param name="restoreCtx"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task<bool> OnDataLossAsync(RestoreContext restoreCtx, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, "OnDataLoss Invoked!");
            this.SetupBackupStore();

            try
            {
                string backupFolder;
                if (this.backupStorageType == BackupManagerType.None)
                {
                    return false;
                }
                else
                {
                    backupFolder = await this.backupStore.RestoreLatestBackupToTempLocation(cancellationToken);
                }

                ServiceEventSource.Current.ServiceMessage(this.Context, "Restoration Folder Path " + backupFolder);
                RestoreDescription restoreRescription = new RestoreDescription(backupFolder, RestorePolicy.Force);
                await restoreCtx.RestoreAsync(restoreRescription, cancellationToken);
                ServiceEventSource.Current.ServiceMessage(this.Context, "Restore completed");
                DirectoryInfo tempRestoreDirectory = new DirectoryInfo(backupFolder);
                tempRestoreDirectory.Delete(true);
                return true;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Restoration failed: " + "{0} {1}" + e.GetType() + e.Message);
                throw;
            }
        }

        /// <summary>
        /// BackupCallbackAsync is called whenever a backup needs
        /// to be taken.
        /// </summary>
        /// <param name="backupInfo"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<bool> BackupCallbackAsync(BackupInfo backupInfo, CancellationToken cancellationToken)
        {
            //ServiceEventSource.Current.ServiceMessage(this.Context, "Inside backup callback for replica {0}|{1}", this.Context.PartitionId, this.Context.ReplicaId);
            long totalBackupCount;

            IReliableDictionary<string, long> backupCountDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>(BackupCountDictionaryName);

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<long> value = await backupCountDictionary.TryGetValueAsync(tx, "backupCount");
                if (!value.HasValue)
                {
                    totalBackupCount = 0;
                }
                else
                {
                    totalBackupCount = value.Value;
                }
                await backupCountDictionary.SetAsync(tx, "backupCount", ++totalBackupCount);
                await tx.CommitAsync();
            }
            //ServiceEventSource.Current.Message("Backup count dictionary updated, total backup count is {0}", totalBackupCount);
            try
            {
                //ServiceEventSource.Current.ServiceMessage(this.Context, "Archiving backup");
                await this.backupStore.ArchiveBackupAsync(backupInfo, cancellationToken);
                //ServiceEventSource.Current.ServiceMessage(this.Context, "Backup archived");
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this.Context, "Archive of backup failed: Source: {0} Exception: {1}", backupInfo.Directory, e.Message);
            }
            await this.backupStore.DeleteBackupsAsync(cancellationToken);
            //ServiceEventSource.Current.Message("Backups deleted");
            return true;
        }

        /// <summary>
        /// PerioducTakeBackupAsync runs in a dedicated thread and will
        /// periodically take backups.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task PeriodicTakeBackupAsync(CancellationToken cancellationToken)
        {
            long backupsTaken = 0;
            this.SetupBackupStore();

            while (true)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (this.backupStorageType == BackupManagerType.None)
                    {
                        break;
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(this.backupStore.backupFrequencyInSeconds), cancellationToken);
                        BackupDescription backupDescription = new BackupDescription(BackupOption.Full, this.BackupCallbackAsync);
                        await this.BackupAsync(backupDescription, TimeSpan.FromHours(1), cancellationToken);
                        backupsTaken++;
                        //ServiceEventSource.Current.ServiceMessage(this.Context, "Backup {0} taken", backupsTaken);
                    }
                }
                catch (FabricNotPrimaryException)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric cannot perform write as it is not the primary replica");
                    return;
                }
                catch (FabricNotReadableException)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric is not currently readable, aborting and will retry");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }
                catch (InvalidOperationException)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid operation performed, aborting and will retry");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }
            }
        }

        /// <summary>
        /// Initialise backup store
        /// </summary>
        private void SetupBackupStore()
        {
            string partitionId = this.Context.PartitionId.ToString("N");
            long minKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).LowKey;
            long maxKey = ((Int64RangePartitionInformation)this.Partition.PartitionInfo).HighKey;

            if (this.Context.CodePackageActivationContext != null)
            {
                ICodePackageActivationContext codePackageContext = this.Context.CodePackageActivationContext;
                ConfigurationPackage configPackage = codePackageContext.GetConfigurationPackageObject("Config");
                ConfigurationSection configSection = configPackage.Settings.Sections["Inventory.Service.Settings"];

                string backupSettingValue = configSection.Parameters["BackupMode"].Value;

                if (string.Equals(backupSettingValue, "none", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.None;
                }
                else if (string.Equals(backupSettingValue, "azure", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.Azure;

                    ConfigurationSection azureBackupConfigSection = configPackage.Settings.Sections["Inventory.Service.BackupSettings.Azure"];

                    this.backupStore = new AzureBackupStore(azureBackupConfigSection, partitionId, minKey, maxKey, codePackageContext.TempDirectory);
                }
                else if (string.Equals(backupSettingValue, "local", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.backupStorageType = BackupManagerType.Local;

                    ConfigurationSection localBackupConfigSection = configPackage.Settings.Sections["Inventory.Service.BackupSettings.Local"];

                    this.backupStore = new DiskBackupStore(localBackupConfigSection, partitionId, minKey, maxKey, codePackageContext.TempDirectory);
                }
                else
                {
                    throw new ArgumentException("Unknown backup type");
                }

                ServiceEventSource.Current.ServiceMessage(this.Context, "Backup Manager Set Up");
            }
        }

        /// <summary>
        /// Backup enum types
        /// </summary>
        private enum BackupManagerType
        {
            Azure,
            Local,
            None
        };
    }

    #endregion
}