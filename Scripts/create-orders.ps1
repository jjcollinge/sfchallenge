<#
.SYNOPSIS 
Tests the end to end functionality of the Exchange app.

.DESCRIPTION
This script tests the end to end functionality of the Exchange app by creating 2 users and 'n' orders. It then tests that the 'n' orders are successfully recored in the trade store.

.PARAMETER Domain
The fully qualified domain and reverse proxy port to contact the application i.e. myname.westeurope.cloudapp.azure.com:80

.PARAMETER numTrades
The number of trades to complete.

.PARAMETER useAllCurrencies
Whether to use just GBPUSD or a mix of all currency pairs i.e. GBPUSD, GBPEUR, USDGBP, USDEUR, EURGBP, EURUSD

.PARAMETER isSingleton
Whether the OrderBook is a singleton or not

.PARAMETER requestTimeoutSec
Timeout for HTTP requests to the service

.PARAMETER completeTimeoutSec
Timeout for the script to complete

.EXAMPLE
. Scripts\create-orders.ps1 -numTrades 100 -useAllCurrencies $True -isSingleton $False
#>
Param(
    [string]
    $domain="localhost:19081",
    [int]
    $numTrades=100,
    [bool]
    $useAllCurrencies=$False,
    [int]
    $requestTimeoutSec=20,
    [int]
    $completeTimeoutSec=240,
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
$targetTradeCount = $initialTradeCount + $numTrades

for ($i = 0; $i -lt $numTrades; $i++)
{   
    $index = 0
    if($useAllCurrencies -eq $True)
    {
		$index = (Get-Random -Minimum 0 -Maximum 6)
    }
    $bidId = createBidForUserFromFixture $index $user1Id
    $askId = createAskForUserFromFixture $index $user2Id
}

$status = "successfully"
$complete = $False

while(-not($complete))
{
    Sleep -Seconds 1

    $currentTradeCount = (Invoke-RestMethod -Method Get -Uri "${loggerEndpoint}")
    if ($currentTradeCount -ge $targetTradeCount)
    {
        $complete = $True
    }
    else 
    {
        $remaining = $targetTradeCount - $currentTradeCount
        log "Remaining trades: ${remaining}"
    }

    if (((get-date) - $startTime).TotalSeconds -gt $completeTimeoutSec)
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
log "Order count: ${numTrades}"
