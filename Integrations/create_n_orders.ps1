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
    $completeTimeoutSec=240
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

function createAskForUserFromFixture($index, $fixturePath, $userId)
{
    $fixturePath = "${orderFixturePrefix}.${index}.${orderFixtureSuffix}"
    $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
    $order.userId = $userId
    $orderJSON = ($order | ConvertTo-Json)
    $askOrderId = Invoke-RestMethod -Method Post -Uri $askEndpoint -Body $orderJSON -ContentType "application/json" -TimeoutSec $requestTimeoutSec
    log "Created ask ${askOrderId} from ${fixturePath}"
    return $askOrderId
}

function createBidForUserFromFixture($index, $fixturePath, $userId)
{
    $fixturePath = "${orderFixturePrefix}.${index}.${orderFixtureSuffix}"
    $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
    $order.userId = $userId
    $orderJSON = ($order | ConvertTo-Json)
    $bidOrderId = Invoke-RestMethod -Method Post -Uri $bidEndpoint -Body $orderJSON -ContentType "application/json" -TimeoutSec $requestTimeoutSec
    log "Created bid ${bidOrderId} from ${fixturePath}"
    return $bidOrderId
}

$startTime = $(get-date)

$user1Id = createUserFromFixture $user1Fixture
$user2Id = createUserFromFixture $user2Fixture

$user1 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user1Id" -TimeoutSec $requestTimeoutSec)
$user1GBPAmount = $user1.CurrencyAmounts.GBP
$user1USDAmount = $user1.CurrencyAmounts.USD
$user1EURAmount = $user1.CurrencyAmounts.EUR

$user2 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user2Id" -TimeoutSec $requestTimeoutSec)
$user2GBPAmount = $user2.CurrencyAmounts.GBP
$user2USDAmount = $user2.CurrencyAmounts.USD
$user2EURAmount = $user2.CurrencyAmounts.EUR

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
        $index = (Get-Random -Minimum 0 -Maximum 5)
        Switch ($index) {
            0 { $GBPUSDCount++ }
            1 { $GBPEURCount++ }
            2 { $USDGBPCount++ }
            3 { $USDEURCount++ }
            4 { $EURGBPCount++ }
            5 { $EURUSDCount++ }
        }
    }
    $bidId = createBidForUserFromFixture $index $orderFixture $user1Id
    $askId = createAskForUserFromFixture $index $orderFixture $user2Id
}

$status = "successfully"
$tradeId = "${askId}_${bidId}"
$user1Complete = $False
$user2Complete = $False
while(-not($user1Complete -and $user2Complete))
{
    Sleep -Seconds 1
    $user1 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user1Id")
    $user1GBPDesired = ($user1GBPAmount + $GBPUSDCount + $GBPEURCount - $USDGBPCount - $EURGBPCount)
    $user1USDDesired = ($user1USDAmount + $USDGBPCount + $USDEURCount - $GBPUSDCount - $EURUSDCount)
    $user1EURDesired = ($user1EURAmount + $EURGBPCount + $EURUSDCount - $USDEURCount - $GBPEURCount)
    [int]$user1GBPRemaining = $user1GBPDesired - $user1.CurrencyAmounts.GBP
    [int]$user1USDRemaining = $user1USDDesired - $user1.CurrencyAmounts.USD
    [int]$user1EURRemaining = $user1EURDesired - $user1.CurrencyAmounts.EUR
    $user1Complete = (([int]$user1GBPRemaining -eq 0) -and ([int]$user1USDRemaining -eq 0) -and ([int]$user1EURRemaining -eq 0))
    if ($user1GBPRemaining -lt 0) {$user1GBPRemaining *= -1}
    if ($user1USDRemaining -lt 0) {$user1USDRemaining *= -1}
    if ($user1EURRemaining -lt 0) {$user1EURRemaining *= -1}
    log "User1 status: complete = ${user1Complete}"  
    log "User1 outstanding orders: GBP[${user1GBPRemaining}], USD[${user1USDRemaining}], EUR[${user1EURRemaining}]"
    
    $user2 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user2Id")
    $user2GBPDesired = ($user2GBPAmount - $GBPUSDCount - $GBPEURCount + $USDGBPCount + $EURGBPCount)
    $user2USDDesired = ($user2USDAmount - $USDGBPCount - $USDEURCount + $GBPUSDCount + $EURUSDCount)
    $user2EURDesired = ($user2EURAmount - $EURGBPCount - $EURUSDCount + $USDEURCount + $GBPEURCount)
    [int]$user2GBPRemaining = $user2GBPDesired - $user2.CurrencyAmounts.GBP
    [int]$user2USDRemaining = $user2USDDesired - $user2.CurrencyAmounts.USD
    [int]$user2EURRemaining = $user2EURDesired - $user2.CurrencyAmounts.EUR
    $user2Complete = (([int]$user2GBPRemaining -eq 0) -and ([int]$user2USDRemaining -eq 0) -and ([int]$user2EURRemaining -eq 0))
    if ($user2GBPRemaining -lt 0) {$user2GBPRemaining *= -1}
    if ($user2USDRemaining -lt 0) {$user2USDRemaining *= -1}
    if ($user2EURRemaining -lt 0) {$user2EURRemaining *= -1}
    log "User2 status: complete = ${user2Complete}"  
    log "User2 outstanding orders: GBP[${user2GBPRemaining}], USD[${user2USDRemaining}], EUR[${user2EURRemaining}]"

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
