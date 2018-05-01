using Microsoft.ServiceFabric.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UserStore
{
    public interface IBackupStore
    {
        long backupFrequencyInSeconds { get; }
        Task ArchiveBackupAsync(BackupInfo backupInfo, CancellationToken cancellationToken);
        Task<string> RestoreLatestBackupToTempLocation(CancellationToken cancellationToken);
        Task DeleteBackupsAsync(CancellationToken cancellationToken);
    }
}
