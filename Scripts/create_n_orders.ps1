<#
	Name: create_n_orders.ps1

	Params:
		domain:				The reverse proxy endpoint for your cluster
		n:					The number of trades to send
		useAllCurrencies:	Send orders of just 1 currency or all currencies
		isSingleton:		Is the OrderBook service a singleton or not?
		requestTimeoutSec:	Timeout length in seconds for each request
		completeTimeoutSec: Timeout length in seconds for script completion
#>
Param(
    [string]
    $domain="localhost:19081",
    [int]
    $n=100,
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

$user1 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user1Id" -TimeoutSec $requestTimeoutSec)
$user1GBPInitial = $user1.CurrencyAmounts.GBP
$user1USDInitial = $user1.CurrencyAmounts.USD
$user1EURInitial = $user1.CurrencyAmounts.EUR

$user2 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user2Id" -TimeoutSec $requestTimeoutSec)
$user2GBPInitial = $user2.CurrencyAmounts.GBP
$user2USDInitial = $user2.CurrencyAmounts.USD
$user2EURInitial = $user2.CurrencyAmounts.EUR

$bidId = ""
$askId = ""

$GBPUSDCount = 0
$GBPEURCount = 0
$USDGBPCount = 0
$USDEURCount = 0
$EURUSDCount = 0
$EURGBPCount = 0

for ($i = 0; $i -lt $n; $i++)
{   
    $index = 0
    if($useAllCurrencies -eq $True)
    {
		$index = (Get-Random -Minimum 0 -Maximum 6)
		Switch ($index) {
			0 { $GBPUSDCount++ }
			1 { $GBPEURCount++ }
			2 { $USDGBPCount++ }
			3 { $USDEURCount++ }
			4 { $EURGBPCount++ }
			5 { $EURUSDCount++ }
		}
    }
	else
	{
		$GBPUSDCount = $n
	}
    $bidId = createBidForUserFromFixture $index $user1Id
    $askId = createAskForUserFromFixture $index $user2Id
}

$status = "successfully"
$tradeId = "${askId}_${bidId}"
$user1Complete = $False
$user2Complete = $False

if ($isSingleton -eq $True) {
	$GBPUSDCount = $n
	$GBPEURCount = 0
	$USDGBPCount = 0
	$USDEURCount = 0
	$EURUSDCount = 0
	$EURGBPCount = 0
}

[int64]$user1GBPDesired = ($user1GBPInitial + $GBPUSDCount + $GBPEURCount - $USDGBPCount - $EURGBPCount)
[int64]$user1USDDesired = ($user1USDInitial + $USDGBPCount + $USDEURCount - $GBPUSDCount - $EURUSDCount)
[int64]$user1EURDesired = ($user1EURInitial + $EURGBPCount + $EURUSDCount - $USDEURCount - $GBPEURCount)

[int64]$user2GBPDesired = ($user2GBPInitial - $GBPUSDCount - $GBPEURCount + $USDGBPCount + $EURGBPCount)
[int64]$user2USDDesired = ($user2USDInitial - $USDGBPCount - $USDEURCount + $GBPUSDCount + $EURUSDCount)
[int64]$user2EURDesired = ($user2EURInitial - $EURGBPCount - $EURUSDCount + $USDEURCount + $GBPEURCount)

while(-not($user1Complete -and $user2Complete))
{
    Sleep -Seconds 1

    $user1 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user1Id")
	[int64]$user1GBPCurrent = $user1.CurrencyAmounts.GBP
	[int64]$user1USDCurrent = $user1.CurrencyAmounts.USD
	[int64]$user1EURCurrent = $user1.CurrencyAmounts.EUR
    [int64]$user1GBPRemaining = $user1GBPDesired - $user1GBPCurrent
    [int64]$user1USDRemaining = $user1USDDesired - $user1USDCurrent
    [int64]$user1EURRemaining = $user1EURDesired - $user1EURCurrent
    $user1Complete = (([int64]$user1GBPRemaining -eq 0) -and ([int64]$user1USDRemaining -eq 0) -and ([int64]$user1EURRemaining -eq 0))
	log "User1"
	log "-------------"
	log "Target GBP: ${user1GBPDesired}, Current GBP: ${user1GBPCurrent}, Diff: ${user1GBPRemaining}"
	log "Target USD: ${user1USDDesired}, Current USD: ${user1USDCurrent}, Diff: ${user1USDRemaining}"
	log "Target EUR: ${user1EURDesired}, Current EUR: ${user1EURCurrent}, Diff: ${user1EURRemaining}"
    log "Status: Complete = ${user1Complete}"  
	log "-------------"
    
    $user2 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user2Id")
    [int64]$user2GBPCurrent = $user2.CurrencyAmounts.GBP
	[int64]$user2USDCurrent = $user2.CurrencyAmounts.USD
	[int64]$user2EURCurrent = $user2.CurrencyAmounts.EUR
    [int64]$user2GBPRemaining = $user2GBPDesired - $user2GBPCurrent
    [int64]$user2USDRemaining = $user2USDDesired - $user2USDCurrent
    [int64]$user2EURRemaining = $user2EURDesired - $user2EURCurrent
    $user2Complete = (([int64]$user2GBPRemaining -eq 0) -and ([int64]$user2USDRemaining -eq 0) -and ([int64]$user2EURRemaining -eq 0))
    log "User2"
	log "-------------"
	log "Target GBP: ${user2GBPDesired}, Current GBP: ${user2GBPCurrent}, Diff: ${user2GBPRemaining}"
	log "Target USD: ${user2USDDesired}, Current USD: ${user2USDCurrent}, Diff: ${user2USDRemaining}"
	log "Target EUR: ${user2EURDesired}, Current EUR: ${user2EURCurrent}, Diff: ${user2EURRemaining}"
    log "Status: Complete = ${user2Complete}"
	log "-------------"

    if (((get-date) - $startTime).TotalSeconds -gt $completeTimeoutSec)
    {
        log "Timing out..."
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
log "Order count: ${n}"
