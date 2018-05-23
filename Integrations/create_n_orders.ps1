Param(
    [string]
    $domain="localhost:19081",
    [int]
    $n=100
)

$ordersSvcEndpoint = "http://${domain}/Exchange/Gateway"
$fulfillmentSvcEndpoint = "http://${domain}/Exchange/Gateway"
$bidEndpoint = "${ordersSvcEndpoint}/api/orders/bid"
$askEndpoint = "${ordersSvcEndpoint}/api/orders/ask"
$ordersEndpoint = "${ordersSvcEndpoint}/api/orders"
$transfersEndpoint = "${fulfillmentSvcEndpoint}/api/trades"
$usersEndpoint = "${fulfillmentSvcEndpoint}/api/users"
$user1Fixture = ".\fixtures\user1.json"
$user2Fixture = ".\fixtures\user2.json"
$orderFixture = ".\fixtures\order.json"

function log
{
    Param ([string] $message)
    $time = (Get-Date).ToString('MM/dd/yyyy hh:mm:ss tt')
    Write-Host "[${time}] ${message}"
}

function createUserFromFixture($fixturePath)
{
    $user = (Get-Content $fixturePath | Out-String)
    $userId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $user -ContentType "application/json"
    return $userId
}

function createAskForUserFromFixture($fixturePath, $userId)
{
    $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
    $order.userId = $userId
    $orderJSON = ($order | ConvertTo-Json)
    $askOrderId = Invoke-RestMethod -Method Post -Uri $askEndpoint -Body $orderJSON -ContentType "application/json"
    return $askOrderId
}

function createBidForUserFromFixture($fixturePath, $userId)
{
    $order = (Get-Content $fixturePath | Out-String | ConvertFrom-Json)
    $order.userId = $userId
    $orderJSON = ($order | ConvertTo-Json)
    $bidOrderId = Invoke-RestMethod -Method Post -Uri $bidEndpoint -Body $orderJSON -ContentType "application/json"
    return $bidOrderId
}

$startTime = $(get-date)

$user1Id = createUserFromFixture $user1Fixture
$user2Id = createUserFromFixture $user2Fixture

$user1 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user1Id")
$user1GBPAmount = ($user1.CurrencyAmounts.GBP)

$user2 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user2Id")
$user2GBPAmount = ($user2.CurrencyAmounts.GBP)

$bidId = ""
$askId = ""

for ($i = 0; $i -lt $n; $i++)
{
    $bidId = createBidForUserFromFixture $orderFixture $user1Id
    log "Created bid ${bidId}"
    $askId = createAskForUserFromFixture $orderFixture $user2Id
    log "Created ask ${askId}"
}

$status = "successfully"
$tradeId = "${askId}_${bidId}"
$user1Complete = $False
$user2Complete = $False
while(-not $user1Complete -and -not $user2Complete)
{
    Sleep -Seconds 1
    $user1 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user1Id")
    $user1Current = ($user1.CurrencyAmounts.GBP)
    $user1Desired = ($user1GBPAmount + $n)
    $user1Remaining = $user1Desired - $user1Current
    log "User1 has ${user1Remaining} orders remaining"
    $user1Complete = ($user1Remaining -lt 0.9)
    
    $user2 = (Invoke-RestMethod -Method Get -Uri "${usersEndpoint}/$user2Id")
    $user2Current = ($user2.CurrencyAmounts.GBP)
    $user2Desired = ($user2GBPAmount - $n)
    $user2Remaining = $user2Current - $user2Desired
    log "User2 has ${user2Remaining} orders remaining"
    $user2Complete = ($user2Remaining -lt 0.9)

    if ($startTime.Second -gt $startTime.Second + 300)
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
log "------------"
log "Start time: ${startTime}"
log "End time: ${endTime}"
log "Total time: ${totalTime}"
log "Order count: ${n}"
