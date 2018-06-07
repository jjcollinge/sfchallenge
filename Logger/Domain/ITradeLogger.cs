using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Logger
{
    public interface ITradeLogger
    {
        Task InsertAsync(Trade trade, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
        Task<long> CountAsync(CancellationToken cancellationToken);
    }
}
