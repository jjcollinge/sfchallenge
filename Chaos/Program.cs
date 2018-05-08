using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Fabric;

using System.Diagnostics;
using System.Fabric.Chaos.DataStructures;
using System.Fabric.Health;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Chaos
{
    class Program
    {
        private class ChaosEventComparer : IEqualityComparer<ChaosEvent>
        {
            public bool Equals(ChaosEvent x, ChaosEvent y)
            {
                return x.TimeStampUtc.Equals(y.TimeStampUtc);
            }
            public int GetHashCode(ChaosEvent obj)
            {
                return obj.TimeStampUtc.GetHashCode();
            }
        }
        static X509Credentials GetCredentials(string clientCertThumb, string serverCertThumb, string name)
        {
            X509Credentials xc = new X509Credentials();
            xc.StoreLocation = StoreLocation.CurrentUser;
            xc.StoreName = "My";
            xc.FindType = X509FindType.FindByThumbprint;
            xc.FindValue = clientCertThumb;
            xc.RemoteCommonNames.Add(name);
            xc.RemoteCertThumbprints.Add(serverCertThumb);
            xc.ProtectionLevel = ProtectionLevel.EncryptAndSign;
            return xc;
        }

        static void Main(string[] args)
        {
            // README:
            //
            // Please ensure your cluster certificate is installed in
            // the 'CurrentUser' certificate store.
            //
            // REQUIRED STEPS:
            // - Paste your Service Fabric certificate's thumbprint below (line 52,53)
            // - Update the cluster domain name to match your SF cluster (line 54)
            // - Add your cluster node types to the inclusion list (line 102)

            string clientCertThumb = "7a16201c6df8cec4992925d6f290fd4d31b9a0d8";
            string serverCertThumb = "7a16201c6df8cec4992925d6f290fd4d31b9a0d8";
            string clusterDomainName = "win243dbhqilqz.westus.cloudapp.azure.com";

            string commonName = $"www.{clusterDomainName}";          
            string clusterEndpoint = $"{clusterDomainName}:19000";

            var creds = GetCredentials(clientCertThumb, serverCertThumb, commonName);

            Console.WriteLine($"Connecting to cluster {clusterEndpoint} using certificate '{clientCertThumb}'.");
            using (var client = new FabricClient(creds, clusterEndpoint))
            {
                var startTimeUtc = DateTime.UtcNow;

                // The maximum amount of time to wait for all cluster entities to become stable and healthy. 
                // Chaos executes in iterations and at the start of each iteration it validates the health of cluster entities. 
                // During validation if a cluster entity is not stable and healthy within MaxClusterStabilizationTimeoutInSeconds, Chaos generates a validation failed event.
                var maxClusterStabilizationTimeout = TimeSpan.FromSeconds(30.0);

                var timeToRun = TimeSpan.FromMinutes(60.0);

                // MaxConcurrentFaults is the maximum number of concurrent faults induced per iteration. 
                // Chaos executes in iterations and two consecutive iterations are separated by a validation phase. 
                // The higher the concurrency, the more aggressive the injection of faults -- inducing more complex series of states to uncover bugs. 
                // The recommendation is to start with a value of 2 or 3 and to exercise caution while moving up.
                var maxConcurrentFaults = 3;

                // Describes a map, which is a collection of (string, string) type key-value pairs. The map can be used to record information about
                // the Chaos run. There cannot be more than 100 such pairs and each string (key or value) can be at most 4095 characters long.
                // This map is set by the starter of the Chaos run to optionally store the context about the specific run.
                var startContext = new Dictionary<string, string> { { "ReasonForStart", "Testing" } };

                // Time-separation (in seconds) between two consecutive iterations of Chaos. The larger the value, the lower the fault injection rate.
                var waitTimeBetweenIterations = TimeSpan.FromSeconds(10);

                // Wait time (in seconds) between consecutive faults within a single iteration. 
                // The larger the value, the lower the overlapping between faults and the simpler the sequence of state transitions that the cluster goes through. 
                // The recommendation is to start with a value between 1 and 5 and exercise caution while moving up.
                var waitTimeBetweenFaults = TimeSpan.Zero;

                // Passed-in cluster health policy is used to validate health of the cluster in between Chaos iterations. 
                var clusterHealthPolicy = new ClusterHealthPolicy
                {
                    ConsiderWarningAsError = false,
                    MaxPercentUnhealthyApplications = 100,
                    MaxPercentUnhealthyNodes = 100
                };

                // All types of faults, restart node, restart code package, restart replica, move primary replica, and move secondary replica will happen
                // for nodes of type 'FrontEndType'
                var nodetypeInclusionList = new List<string> { "nt1party" };

                // In addition to the faults included by nodetypeInclusionList, 
                // restart code package, restart replica, move primary replica, move secondary replica faults will happen for 'fabric:/TestApp2'
                // even if a replica or code package from 'fabric:/TestApp2' is residing on a node which is not of type included in nodeypeInclusionList.
                var applicationInclusionList = new List<string> { "fabric:/Exchange" };

                // List of cluster entities to target for Chaos faults.
                var chaosTargetFilter = new ChaosTargetFilter
                {
                    NodeTypeInclusionList = nodetypeInclusionList,
                    ApplicationInclusionList = applicationInclusionList
                };

                var parameters = new ChaosParameters(
                    maxClusterStabilizationTimeout,
                    maxConcurrentFaults,
                    true, /* EnableMoveReplicaFault */
                    timeToRun,
                    startContext,
                    waitTimeBetweenIterations,
                    waitTimeBetweenFaults,
                    clusterHealthPolicy)
                { ChaosTargetFilter = chaosTargetFilter };

                try
                {
                    client.TestManager.StartChaosAsync(parameters).GetAwaiter().GetResult();
                }
                catch (FabricChaosAlreadyRunningException)
                {
                    Console.WriteLine("An instance of Chaos is already running in the cluster. Connect to the cluster and then run Stop-ServiceFabricChaos to stop it.");
                }

                var filter = new ChaosReportFilter(startTimeUtc, DateTime.MaxValue);

                var eventSet = new HashSet<ChaosEvent>(new ChaosEventComparer());

                string continuationToken = null;

                while (true)
                {
                    ChaosReport report;
                    try
                    {
                        report = string.IsNullOrEmpty(continuationToken)
                            ? client.TestManager.GetChaosReportAsync(filter).GetAwaiter().GetResult()
                            : client.TestManager.GetChaosReportAsync(continuationToken).GetAwaiter().GetResult();
                    }
                    catch (Exception e)
                    {
                        if (e is FabricTransientException)
                        {
                            Console.WriteLine("A transient exception happened: '{0}'", e);
                        }
                        else if (e is TimeoutException)
                        {
                            Console.WriteLine("A timeout exception happened: '{0}'", e);
                        }
                        else
                        {
                            throw;
                        }

                        Task.Delay(TimeSpan.FromSeconds(1.0)).GetAwaiter().GetResult();
                        continue;
                    }

                    continuationToken = report.ContinuationToken;

                    foreach (var chaosEvent in report.History)
                    {
                        if (eventSet.Add(chaosEvent))
                        {
                            Console.WriteLine(chaosEvent);
                        }
                    }

                    // When Chaos stops, a StoppedEvent is created.
                    // If a StoppedEvent is found, exit the loop.
                    var lastEvent = report.History.LastOrDefault();

                    if (lastEvent is StoppedEvent)
                    {
                        break;
                    }

                    Task.Delay(TimeSpan.FromSeconds(1.0)).GetAwaiter().GetResult();
                }
            }
        }
    }
}
