Param(
    [string]$domain="localhost:19081"
)

$ordersSvcEndpoint = "http://${domain}/Exchange/Gateway"
$fulfillmentSvcEndpoint = "http://${domain}/Exchange/Fulfillment"
$bidEndpoint = "${ordersSvcEndpoint}/api/orders/bid"
$askEndpoint = "${ordersSvcEndpoint}/api/orders/ask"
$ordersEndpoint = "${ordersSvcEndpoint}/api/orders"
$transfersEndpoint = "${fulfillmentSvcEndpoint}/api/trades?PartitionKey=1&PartitionKind=Int64Range"
$usersEndpoint = "${fulfillmentSvcEndpoint}/api/users?PartitionKey=1&PartitionKind=Int64Range"

function log
{
    Param ([string] $message)
    $time = (Get-Date).ToString('MM/dd/yyyy hh:mm:ss tt')
    Write-Host "[${time}] ${message}"
}

log "orders endpoint: ${ordersSvcEndpoint}"
log "fulfillment endpoint: ${fulfillmentSvcEndpoint}"


<#
Requires work to make this support partitioned workloads...

$users = Invoke-RestMethod -Method Get -Uri $usersEndpoint
if ($users.Count -gt 0)
{
   
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
    
    
    Invoke-RestMethod -Method Delete -Uri $ordersEndpoint
}
#>

# Create buyer
log "creating a new buyer"
$buyer = @{
    'ID' = 'buyer'
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
    'ID' = 'seller'
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

log "begin adding orders"
$options = "bitcoin", "dogcoin", "lawrencecoin", "jonicoin", "anderscoin"
$runCount = 5000
for ($i = 0; $i -lt $runCount; $i++)
{
    $random = Get-Random -Minimum 0 -Maximum 4
    $headers = @{}
    $coin = $options[$random]
    Write-Host "Using coin: $coin"
    $headers.Add("x-item-type",$coin)
    # Create bid
    log "creating a new bid for buyer ${buyerId}"
    $bid = @{
        'value' = 1
        'quantity' = 1
        'userId' = $buyerId
        }| ConvertTo-Json
    $bidId = Invoke-RestMethod -Method Post -Uri $bidEndpoint -Body $bid -Headers $headers -ContentType "application/json"
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
    $askId = Invoke-RestMethod -Method Post -Uri $askEndpoint -Body $ask -ContentType "application/json" -Headers $headers
    if ($askId -eq "")
    {
        log "failed to create new ask for seller ${sellerId}, terminating now"
        exit
    }
}

# Delay long enough for atleast 1 transfer to get to the queue
Write-Host "Waiting for transfers to start flowing through"
Start-Sleep -Seconds 10

$transfers = Invoke-RestMethod -Method Get -Uri $transfersEndpoint
while ($transfers -ne 0)
{
    $transfers = Invoke-RestMethod -Method Get -Uri $transfersEndpoint
    log "transfers currently in queue: ${transfers}"
    Start-Sleep -Seconds 5
}
log "finished processing orders"

log "test script completed ${runCount} orders, terminating now"


