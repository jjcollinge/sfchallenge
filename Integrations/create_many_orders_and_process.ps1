Param(
    [string]$domain="localhost"
)

$ordersSvcEndpoint = "http://${domain}:80"
$fulfillmentSvcEndpoint = "http://${domain}:8080"
$bidEndpoint = "${ordersSvcEndpoint}/api/orders/bid"
$askEndpoint = "${ordersSvcEndpoint}/api/orders/ask"
$ordersEndpoint = "${ordersSvcEndpoint}/api/orders"
$transfersEndpoint = "${fulfillmentSvcEndpoint}/api/transfers"
$usersEndpoint = "${fulfillmentSvcEndpoint}/api/users"

function log
{
    Param ([string] $message)
    $time = (Get-Date).ToString('MM/dd/yyyy hh:mm:ss tt')
    Write-Host "[${time}] ${message}"
}

log "orders endpoint: ${ordersSvcEndpoint}"
log "fulfillment endpoint: ${fulfillmentSvcEndpoint}"

$users = Invoke-RestMethod -Method Get -Uri $usersEndpoint
if ($users.Count -gt 0)
{
    log "found existing users in exchange, if we continue these will be deleted. Do you wish to continue? (y/n)"
     $continue = Read-Host
    if ($continue -ne "y" -Or $continue -ne "Y")
    {
        log "leaving cluster as is, terminating now"
        exit
    }
    foreach ($user in $users)  
    {
        $userId = $user.id
        Invoke-RestMethod -Method Delete -Uri "$usersEndpoint/$userId"
    }
}

$orders = Invoke-RestMethod -Method Get -Uri $ordersEndpoint 
$startAskCount = $orders.asksCount
$startBidCount = $orders.bidsCount

if($startAskCount -gt 0 -Or $startBidCount -gt 0)
{
    log "found existing orders in exchange, if we continue these will be deleted. Do you wish to continue? (y/n)"
     $continue = Read-Host
    if ($continue -ne "y" -Or $continue -ne "Y")
    {
        log "leaving cluster as is, terminating now"
        exit
    }
    
    Invoke-RestMethod -Method Delete -Uri $ordersEndpoint
}

# Create buyer
log "creating a new buyer"
$buyer = @{
    'Balance' = 1000000000
    'Quantity' = 100000000
    'Username' = "buyer"
} | ConvertTo-Json
$buyerId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $buyer -ContentType "application/json" 
if ($buyerId -eq "")
{
    log "error creating buyer, terminating now"
    exit
}

# Create seller
log "creating a new seller"
$seller = @{
    'balance' = 100000000
    'quantity' = 100000000
    'username' = "seller"
} | ConvertTo-Json
$sellerId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $seller -ContentType "application/json" 
if ($sellerId -eq "")
{
    log "error creating seller, terminating now"
    exit
}

# Allow time for users to be created
Sleep(2)

log "begin adding orders"
$runCount = 1000
for ($i = 0; $i -lt $runCount; $i++)
{
    # Create bid
    log "creating a new bid for buyer ${buyerId}"
    $bid = @{
        'value' = 1
        'quantity' = 1
        'userId' = $buyerId
    } | ConvertTo-Json
    $bidId = Invoke-RestMethod -Method Post -Uri $bidEndpoint -Body $bid -ContentType "application/json" 
    if ($bidId -eq "")
    {
        log "failed to create bid for buyer ${buyerId}, terminating now"
        exit
    }

    # Create ask
    log "creating a new ask for seller ${sellerId}"
    $ask = @{
        'value' = 1
        'quantity' = 1
        'userId' = $sellerId
    } | ConvertTo-Json
    $askId = Invoke-RestMethod -Method Post -Uri $askEndpoint -Body $ask -ContentType "application/json" 
    if ($askId -eq "")
    {
        log "failed to create new ask for seller ${sellerId}, terminating now"
        exit
    }
}

$transfers = Invoke-RestMethod -Method Get -Uri $transfersEndpoint
while ($transfers -ne 0)
{
    $transfers = Invoke-RestMethod -Method Get -Uri $transfersEndpoint
    log "transfer count: ${transfers}"
    Start-Sleep -Seconds 5
}
log "finished processing orders"

log "test script completed ${runCount} orders, terminating now"


