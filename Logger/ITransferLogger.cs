using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Logger
{
    public interface ITransferLogger
    {
        Task InsertAsync(Transfer transfer);
        Task ClearAsync();
    }
}
