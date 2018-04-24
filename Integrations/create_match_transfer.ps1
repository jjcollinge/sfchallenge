Param(
    [string]$domain="localhost"
)

$ordersSvcEndpoint = "http://${domain}:80"
$fulfillmentSvcEndpoint = "http://${domain}:8080"
$bidEndpoint = "${ordersSvcEndpoint}/api/orders/bid"
$askEndpoint = "${ordersSvcEndpoint}/api/orders/ask"
$ordersEndpoint = "${ordersSvcEndpoint}/api/orders"
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
    log "cluster is clean, starting tests"
}

$runCount = 10
for ($i = 0; $i -lt $runCount; $i++)
{
    $orders = Invoke-RestMethod -Method Get -Uri $ordersEndpoint 
    $startAskCount = $orders.asksCount
    $startBidCount = $orders.bidsCount

    # Create buyer
    log "creating a new buyer"
    $buyer = @{
        'Balance' = 100
        'Quantity' = 100
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
        'balance' = 100
        'quantity' = 100
        'username' = "seller"
    } | ConvertTo-Json
    $sellerId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $seller -ContentType "application/json" 
    if ($sellerId -eq "")
    {
        log "error creating seller, terminating now"
        exit
    }

    # Create bid
    log "creating a new bid for buyer ${buyerId}"
    $bid = @{
        'value' = 100
        'quantity' = 100
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
        'value' = 100
        'quantity' = 100
        'userId' = $sellerId
    } | ConvertTo-Json
    $askId = Invoke-RestMethod -Method Post -Uri $askEndpoint -Body $ask -ContentType "application/json" 
    if ($askId -eq "")
    {
        log "failed to create new ask for seller ${sellerId}, terminating now"
        exit
    }

    # Check orders created
    $orders = Invoke-RestMethod -Method Get -Uri $ordersEndpoint 
    $afterAskCount = $orders.asksCount
    $afterBidCount = $orders.bidsCount

    # Check ask count has incremented
    $expectedAskCount = $startAskCount + 1
    if (!($afterAskCount -eq $expectedAskCount))
    {
        log "expected the ask count to equal ${expectedAskCount}, but got ${afterAskCount}, terminating now"
        exit
    }

    # Check bid count has incremented
    $expectedBidCount = $startBidCount + 1
    if (!($afterBidCount -eq $expectedBidCount))
    {
        log "expected the bid count to equal ${expectedBidCount}, but got ${afterBidCount}, terminating now"
        exit
    }

    # Wait...
    $waitPeriod = 5
    log "waiting for ${waitPeriod} seconds to allow transfer to be processed..."
    Start-Sleep -Seconds $waitPeriod

    # Check seller updated
    $afterSeller = Invoke-RestMethod -Method Get -Uri "$usersEndpoint/$sellerId"
    $afterSellerTransfersCount = $afterSeller.transfers.Count
    $expectedSellerTransfersCount = $seller.transfers.Count + 1
    if(!($afterSellerTransfersCount -eq $expectedSellerTransfersCount))
    {
        log "expected seller transfers count to equal ${expectedSellerTransfersCount}, but got ${afterSellerTransfersCount}, terminating now"
        exit
    }

    # Check buyer updated
    $afterBuyer = Invoke-RestMethod -Method Get -Uri "$usersEndpoint/$sellerId"
    $afterBuyerTransfersCount = $afterBuyer.transfers.Count
    $expectedBuyerTransfersCount = $buyer.transfers.Count + 1
    if(!($afterBuyerTransfersCount -eq $expectedBuyerTransfersCount))
    {
        log "expected seller transfers count to equal ${expectedBuyerTransfersCount}, but got ${afterBuyerTransfersCount}, terminating now"
        exit
    }

    Invoke-RestMethod -Method Delete -Uri "$usersEndpoint/$sellerId"
    Invoke-RestMethod -Method Delete -Uri "$usersEndpoint/$buyerId"
}

log "test script completed ${runCount} cycles, terminating now"


