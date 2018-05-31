<#
.SYNOPSIS 
Tests the end to end functionality of the Exchange app.

.DESCRIPTION
This script tests the end to end functionality of the Exchange app by creating 2 users and 'n' orders. It then tests that the 'n' orders are successfully recored in the trade store.

.PARAMETER Domain
The fully qualified domain and reverse proxy port to contact the application i.e. myname.westeurope.cloudapp.azure.com:80

.PARAMETER numTradesPerJob
The number of trades to complete.

.PARAMETER useAllCurrencies
Whether to use just GBPUSD or a mix of all currency pairs i.e. GBPUSD, GBPEUR, USDGBP, USDEUR, EURGBP, EURUSD

.PARAMETER isSingleton
Whether the OrderBook is a singleton or not

.PARAMETER requestTimeoutSec
Timeout for HTTP requests to the service

.PARAMETER completeTimeoutSec
Timeout for the script to complete

.PARAMETER waitJobTimeout
Used to set the timeout on the Wait-Job call. If running a large number of trades per job, please set this appropriately.

.EXAMPLE
. Scripts\create-orders.ps1 -numJobs 3 -numTradesPerJob 30 -useAllCurrencies $True -isSingleton $False
#>
Param(
    [string]
    $domain="localhost:19081",
    [int]
    $numJobs=1,
    [int]
    $numTradesPerJob=20,
    [bool]
    $useAllCurrencies=$False,
    [int]
    $requestTimeoutSec=20,
    [int]
    $completeTimeoutSec=900,
    [int]
    $waitJobTimeout=600,
	[bool]
	$isSingleton=$True
)

$ErrorActionPreference = "Stop";

$ordersSvcEndpoint = "http://${domain}/Exchange/Gateway"
$fulfillmentSvcEndpoint = "http://${domain}/Exchange/Gateway"
$bidEndpoint = "${ordersSvcEndpoint}/api/orders/bid"
$askEndpoint = "${ordersSvcEndpoint}/api/orders/ask"
$ordersEndpoint = "${ordersSvcEndpoint}/api/orders"
$transfersEndpoint = "${fulfillmentSvcEndpoint}/api/trades"
$usersEndpoint = "${fulfillmentSvcEndpoint}/api/users"
$loggerEndpoint = "${fulfillmentSvcEndpoint}/api/logger/done"
$user1Fixture = ".\fixtures\user1.json"
$user2Fixture = ".\fixtures\user2.json"
$orderFixturePrefix = ".\fixtures\order"
$orderFixtureSuffix = "json"

function log
{
    Param ([string] $message)
    $time = (Get-Date).ToString('MM/dd/yyyy hh:mm:ss tt')
    Write-Host "[${time}] ${message}"
}

function createUserFromFixture($fixturePath)
{
    $user = (Get-Content $fixturePath | Out-String)
    $userId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $user -ContentType "application/json" -TimeoutSec $requestTimeoutSec
    log "Created user ${userId} from ${fixturePath}"
    return $userId
}

function createAskForUserFromFixture($index, $userId)
{
    $fixturePath = "${orderFixturePrefix}.${index}.${orderFixtureSuffix}"
    $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
    $order.userId = $userId
	$currencyPair = $order.pair
    $orderJSON = ($order | ConvertTo-Json)
    $askOrderId = Invoke-RestMethod -Method Post -Uri "${askEndpoint}/${currencyPair}" -Body $orderJSON -ContentType "application/json" -TimeoutSec $requestTimeoutSec
    log "Created ask ${askOrderId} from ${fixturePath}"
    return $askOrderId
}

function createBidForUserFromFixture($index, $userId)
{
    $fixturePath = "${orderFixturePrefix}.${index}.${orderFixtureSuffix}"
    $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
    $order.userId = $userId
	$currencyPair = $order.pair
    $orderJSON = ($order | ConvertTo-Json)
    $bidOrderId = Invoke-RestMethod -Method Post -Uri "${bidEndpoint}/${currencyPair}" -Body $orderJSON -ContentType "application/json" -TimeoutSec $requestTimeoutSec
    log "Created bid ${bidOrderId} from ${fixturePath}"
    return $bidOrderId
}

if (($useAllCurrencies -eq $False) -and ($isSingleton -eq $False))
{
	log "Error: Invalid configuration"
	log "Reason: If you have a partitioned service, you must use ensure the 'useAllCurrencies' parameter is set to True!"
	exit
}
if ($isSingleton -eq $True)
{
	log "Info: If your OrderBook service is partitioned, please ensure the configuration value 'isSingleton' is set to False!"
}

$startTime = $(get-date)

$user1Id = createUserFromFixture $user1Fixture
$user2Id = createUserFromFixture $user2Fixture

$initialTradeCount = (Invoke-RestMethod -Method Get -Uri "${loggerEndpoint}")
$totalTrades = ($numJobs * $numTradesPerJob)
$targetTradeCount = $initialTradeCount + $totalTrades

for ($i = 0; $i -lt $numJobs; $i++)
{
	$jobNum = $i + 1;
    Write-Host "Creating Job ${jobNum}/${numJobs}";

    Start-Job -Name "Job-${i}" -ScriptBlock {

        param([int]$numTradesPerJob, [bool] $useAllCurrencies, 
        [string] $user1Id, [string] $user2Id, 
        [string]$usersEndpoint, [int]$requestTimeoutSec, 
        [string]$askEndpoint, [string] $orderFixturePrefix, 
        [string] $orderFixtureSuffix,
        [string] $bidEndpoint,
        [string] $path)

        $ErrorActionPreference = "Stop";

        function log
        {
            Param ([string] $message)
            $time = (Get-Date).ToString('MM/dd/yyyy hh:mm:ss tt')
            Write-Host "[${time}] ${message}"
        }

        function createAskForUserFromFixture($index, $userId)
        {
            $fixturePath = "${orderFixturePrefix}.${index}.${orderFixtureSuffix}"
            $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
            $order.userId = $userId
	        $currencyPair = $order.pair
            $orderJSON = ($order | ConvertTo-Json)
            $askOrderId = Invoke-RestMethod -Method Post -Uri "${askEndpoint}/${currencyPair}" -Body $orderJSON -ContentType "application/json" -TimeoutSec $requestTimeoutSec
            log "Created ask ${askOrderId} from ${fixturePath}"
            return $askOrderId
        }

        function createBidForUserFromFixture($index, $userId)
        {
            $fixturePath = "${orderFixturePrefix}.${index}.${orderFixtureSuffix}"
            $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
            $order.userId = $userId
	        $currencyPair = $order.pair
            $orderJSON = ($order | ConvertTo-Json)
            $bidOrderId = Invoke-RestMethod -Method Post -Uri "${bidEndpoint}/${currencyPair}" -Body $orderJSON -ContentType "application/json" -TimeoutSec $requestTimeoutSec
            log "Created bid ${bidOrderId} from ${fixturePath}"
            return $bidOrderId
        }

        Set-Location -Path $path
        for ($i = 0; $i -lt $numTradesPerJob; $i++)
        {   
            $index = 0
            if($useAllCurrencies -eq $True)
            {
		        $index = (Get-Random -Minimum 0 -Maximum 6)
            }
            $bidId = createBidForUserFromFixture $index $user1Id
            $askId = createAskForUserFromFixture $index $user2Id
        }
    } -ArgumentList ($numTradesPerJob,$useAllCurrencies,$user1Id,$user2Id, $usersEndpoint, $requestTimeoutSec, $askEndpoint, $orderFixturePrefix, $orderFixtureSuffix, $bidEndpoint,  (Get-Location).Path ) 
       
}

Try
{
    # Wait for all jobs
    Get-Job | Wait-Job -TimeoutSec $waitJobTimeout

    # Get all jobs
    Get-Job | Receive-Job | Out-GridView
}
Finally
{
    # Remove all jobs
    Get-Job | Remove-Job -Force
}

log "All Jobs complete"

$status = "successfully"
$complete = $False

while(-not($complete))
{
    sleep -seconds 1

    $currenttradecount = (invoke-restmethod -method get -uri "${loggerendpoint}")
    if ($currenttradecount -ge $targettradecount)
    {
        $complete = $true
    }
    else 
    {
        $remaining = $targettradecount - $currenttradecount
        log "Remaining trades: ${remaining}"
    }

    if (((get-date) - $starttime).totalseconds -gt $completetimeoutsec)
    {
        log "Timed out. Assume Exchange dropped some trades due to validation. Please retry."
        $status = "unsuccessfully, timed out"
        break
    }
}

$endTime = $(get-date)
$elapsedTime = $endTime - $startTime
$totalTime = "{0:HH:mm:ss}" -f ([datetime]$elapsedTime.Ticks)

log "Run complete ${status}"
log "--------------------------"
log "Start time: ${startTime}"
log "End time: ${endTime}"
log "Total time: ${totalTime}"
log "Order count: ${totalTrades}"
